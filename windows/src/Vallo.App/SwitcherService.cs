using System.Collections.Concurrent;
using Vallo.Core;
using static Vallo.App.Native;

namespace Vallo.App;

/// Свитчер целиком: перехват → движок → очередь действий → исполнитель; плюс слежение за раскладкой.
///
/// Потоки:
/// - перехват (KeyboardHook): сортирует событие, спрашивает движок, решает «пропустить/проглотить», кладёт в очередь;
/// - исполнитель: SendInput и смена раскладки по очереди, строго по порядку;
/// - слежение: раскладка активного окна раз в 250 мс и сразу при смене окна;
/// - охранник (ContextGuard): где молчим — считает на своих потоках, перехват только читает ответ.
/// Движок трогают перехват, исполнитель и слежение — только под `gate`.
/// Пока в очереди что-то есть, все остальные нажатия тоже идут через очередь — иначе следующая буква
/// обгонит наше исправление.
sealed class SwitcherService : IHookSink, IDisposable
{
    const int PollMs = 250;
    /// Сколько ждём, пока окно применит нашу смену раскладки, прежде чем верить «расхождению».
    const int SwitchGraceMs = 1000;

    readonly object gate = new();
    readonly Engine engine;
    readonly SystemLayouts layouts;
    readonly SettingsStore store;
    readonly InputSender sender;
    readonly KeyboardHook hook;
    readonly ContextGuard guard;
    readonly SelectionReader selection;

    abstract record Work;
    sealed record Run(IReadOnlyList<EngineAction> Actions) : Work;
    sealed record Replay(HookKey Key) : Work;
    /// Двойной Shift без набранного: перевести выделение (Ctrl+C → перевод → Ctrl+V).
    sealed record ConvertSelection : Work;
    /// Сочетание «исправить выделенный текст»: типографика выделения (Ctrl+C → правка → Ctrl+V).
    sealed record FormatSelection : Work;

    readonly BlockingCollection<Work> queue = new();
    /// Сколько работы в очереди и в исполнении.
    int pending;
    readonly Thread worker;

    readonly Thread watcher;
    readonly AutoResetEvent poke = new(false);
    volatile bool stopping;
    nint lastHkl;
    /// Когда движок последний раз сменил язык или мы попросили окно сменить раскладку. Только под `gate`.
    long lastSwitchTick;

    // --- только поток перехвата ---
    DoubleTapDetector tap; // изменяемая структура: только поле, никаких копий
    bool hasTap;
    /// Второе сочетание — «исправить выделенный текст» (по умолчанию двойной правый Shift).
    DoubleTapDetector correctTap;
    bool hasCorrectTap;
    /// Типографика и смайлики на лету — память последних символов на экране. Только под `gate`.
    readonly LiveTypographer live = new();
    const uint VK_BACK = 0x08, VK_RETURN = 0x0D;
    /// В окне настроек сменили сочетание — поток перехвата пересоберёт детектор при следующем модификаторе.
    volatile bool hotkeyChanged;
    bool lShift, rShift, lCtrl, rCtrl, lAlt, rAlt, lWin, rWin;
    /// Клавиши, чьё нажатие проглотили, — отпускание глотаем тоже.
    readonly HashSet<uint> swallowed = new();

    SwitcherService(SettingsStore store, SystemLayouts layouts, Engine engine)
    {
        this.store = store;
        this.layouts = layouts;
        this.engine = engine;
        sender = new InputSender(layouts, store, MarkSwitch);
        selection = new SelectionReader(sender);
        guard = new ContextGuard(store);
        LoadHotkeys();
        hook = new KeyboardHook(this);
        worker = new Thread(WorkLoop) { IsBackground = true, Name = "Vallo input", Priority = ThreadPriority.AboveNormal };
        watcher = new Thread(Watch) { IsBackground = true, Name = "Vallo layouts" };
    }

    /// null — свитчер не запустить, `problem` — почему (покажем в меню трея). Трей живёт и без него.
    public static SwitcherService? Start(SettingsStore store, out StartProblem problem)
    {
        problem = StartProblem.Unknown;
        var data = Path.Combine(AppContext.BaseDirectory, "Data", "wordlists");
        string[] files = ["en.txt", "ru.txt", "ru-modern.txt"];
        if (!files.All(f => File.Exists(Path.Combine(data, f))))
        {
            problem = StartProblem.NoData;
            return null;
        }
        var layouts = SystemLayouts.Create();
        if (layouts is null)
        {
            problem = StartProblem.NoLayouts;
            return null;
        }

        Lexicon en, ru;
        try
        {
            en = Lexicon.Load(Path.Combine(data, "en.txt"), Language.En);
            ru = Lexicon.Load(Path.Combine(data, "ru.txt"), Language.Ru,
                extra: WordForms.Load(Path.Combine(data, "ru-modern.txt")));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = StartProblem.NoData;
            return null;
        }
        var judge = new Judge(en, ru, learned: new HashSet<string>(store.Current.LearnedWords));
        try { WarmUp(layouts.Pair, en, ru); }
        catch { } // прогрев не удался — первое нажатие будет медленнее, но свитчер работает
        var hkl = SystemLayouts.Current();
        var current = SystemLayouts.LanguageOf(hkl);
        var engine = new Engine(layouts.Pair, judge, current ?? Language.En)
        {
            AutoSwitchEnabled = store.Current.AutoSwitchEnabled,
        };
        if (current is null) engine.ExternalLayoutChange(null);

        var service = new SwitcherService(store, layouts, engine) { lastHkl = hkl };
        service.WarmUpHookPath();
        service.guard.Start();
        service.worker.Start();
        if (!service.hook.Start())
        {
            service.Dispose();
            problem = StartProblem.NoHook;
            return null;
        }
        service.watcher.Start();
        return service;
    }

    /// Проснулись или разблокировали сеанс — перехват мог тихо пропасть.
    public void ReinstallHooks() => hook.Reinstall();

    public void Dispose()
    {
        stopping = true;
        hook.Dispose();
        poke.Set();
        if (watcher.IsAlive) watcher.Join(1000);
        queue.CompleteAdding();
        if (worker.IsAlive) worker.Join(1000);
        guard.Dispose();
        selection.Dispose();
        poke.Dispose();
    }

    // ---- Прогрев: первое нажатие не должно ждать JIT, иначе Windows снимет перехват по таймауту

    /// Отдельный движок на тех же словарях — живой не трогаем.
    static void WarmUp(LayoutPair pair, Lexicon en, Lexicon ru)
    {
        var dummy = new Engine(pair, new Judge(en, ru, new HashSet<string>()), Language.En);
        // «ghbdtn » и «,eltn » — исправление посреди и на границе слова; Backspace, Enter, двойной Shift
        ushort[] keys = [0x47, 0x48, 0x42, 0x44, 0x54, 0x4E, 0x20, 0xBC, 0x45, 0x4C, 0x54, 0x4E, 0x20, 0x51, 0x08, 0x0D];
        foreach (var vk in keys) dummy.HandleKey(new KeyEvent(new KeyStroke(vk), HasCtrlOrAlt: false));
        dummy.HandleKey(new KeyEvent(new KeyStroke(0x47), HasCtrlOrAlt: false));
        dummy.HandleDoubleShift(null);
        dummy.HandleKey(new KeyEvent(new KeyStroke(0x47), HasCtrlOrAlt: true));
        dummy.ExternalLayoutChange(Language.Ru);
        // типографика: регулярные выражения собираются при первом вызове — не на первом нажатии человека
        var live = new LiveTypographer();
        live.Reset(knownLineStart: true);
        var options = new LiveOptions(Typography: true, Emoji: EmojiBrackets.Millennial);
        foreach (var c in TextElements.Split("раз - \"да\" 340000 89161234567 ок)) ")) live.Typed(c, Language.Ru, options);
        live.Backspace();
        Typographer.Format("раз - \"да\" 5000р 89161234567");
    }

    /// Свой код перехвата — скомпилировать заранее.
    void WarmUpHookPath()
    {
        foreach (var name in new[] { nameof(OnKey), nameof(PassOrQueue), nameof(Enqueue), nameof(Modifier), nameof(DoubleTap),
                     nameof(LiveEdit), nameof(CorrectHotkey), nameof(OtherInputForTaps), nameof(Step), nameof(LoadHotkeys),
                     nameof(TrackModifier), nameof(ResyncModifiers), nameof(HasSwitch), nameof(OnMouseDown), nameof(OnForegroundChanged),
                     nameof(OnFocusChanged) })
        {
            var m = typeof(SwitcherService).GetMethod(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (m is not null) System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(m.MethodHandle);
        }
        HookFilter.Classify(0x41, 0, 0, false, false, false);
        _ = guard.AutoSwitchAllowed();
        _ = guard.ManualAllowed();
        _ = guard.InExcludedApp;
        _ = SelectionPolicy.MayCopy(false, guard.ExeName, guard.WindowClass, false);
        _ = GetKeyState(VK_CAPITAL);
        _ = GetAsyncKeyState(0xA0);
    }

    // ---- Перехват (быстро!)

    public bool OnKey(in HookKey k)
    {
        if (k.ExtraInfo == HookFilter.OurMarker) return false;
        TrackModifier(k.Vk, k.Down);
        // отпускание модификатора могло не дойти (Win+L, Ctrl+Alt+Del, UAC) — сверяемся с системой
        if (k.Down && AnyModifierHeld() && !IsModifierVk(k.Vk)) ResyncModifiers();
        var decision = HookFilter.Classify(k.Vk, k.Flags, k.ExtraInfo, lCtrl || rCtrl, lAlt || rAlt, lWin || rWin);
        switch (decision)
        {
            case HookDecision.Ignore:
                return false;
            case HookDecision.ResetBuffer:
                OtherInputForTaps();
                if (k.Down) lock (gate) { engine.Reset(); live.Reset(); }
                // клавишу, чьё нажатие проглотили, отпустили уже с Ctrl/Alt — отпускание тоже глотаем
                if (!k.Down && swallowed.Remove(k.Vk)) return true;
                return PassOrQueue(k);
            case HookDecision.Modifier:
                var fired = Modifier(k);
                var result = PassOrQueue(k);
                if (fired == Fired.Convert) DoubleTap();
                else if (fired == Fired.Correct) CorrectHotkey();
                return result;
        }

        // обычная клавиша
        if (!k.Down) return swallowed.Remove(k.Vk) || PassOrQueue(k);
        OtherInputForTaps();
        // игра (на весь экран или «тихая»), поле пароля — не вмешиваемся и ничего не запоминаем
        if (!guard.ManualAllowed())
        {
            lock (gate) { engine.Reset(); live.Reset(); }
            return PassOrQueue(k);
        }
        var stroke = new KeyStroke((ushort)k.Vk, lShift || rShift, (GetKeyState(VK_CAPITAL) & 1) != 0);
        var settings = store.Current;
        var options = new LiveOptions(settings.TypographyLive, settings.EmojiBrackets());
        var liveOn = (options.Typography || options.Emoji is not null) && guard.TypographyAllowed();
        EngineOutput output;
        IReadOnlyList<EngineAction>? typography = null;
        lock (gate)
        {
            // Backspace сразу после правки на лету — вернуть набранное (как на Маке)
            if (k.Vk == VK_BACK && liveOn && live.Backspace() is { } undo)
            {
                engine.Reset();
                typography = [new EngineAction.Delete(undo.Delete), new EngineAction.Insert(undo.Insert)];
            }
            if (typography is null)
            {
                // исключённое приложение: набор помним (двойной Shift его переведёт), сами не переключаем — как на Маке
                engine.AutoSwitchEnabled = settings.AutoSwitchEnabled && !guard.InExcludedApp;
                output = engine.HandleKey(new KeyEvent(stroke, HasCtrlOrAlt: false));
                if (output is EngineOutput.Consume c)
                {
                    if (HasSwitch(c.Actions)) lastSwitchTick = Environment.TickCount64;
                    // свитчер правил текст сам — на этом нажатии типографика молчит, память начинаем заново
                    live.Reset();
                }
                else if (k.Vk != VK_BACK)
                {
                    typography = liveOn ? LiveEdit(stroke, options) : null;
                    if (!liveOn) live.Reset();
                }
            }
            else output = EngineOutput.PassThrough;
        }
        if (typography is not null)
        {
            swallowed.Add(k.Vk);
            Enqueue(new Run(typography));
            return true;
        }
        if (output is not EngineOutput.Consume consume) return PassOrQueue(k);
        swallowed.Add(k.Vk);
        Enqueue(new Run(consume.Actions));
        return true;
    }

    /// Символ нажатия → правка типографики на лету (или null — пропустить нажатие как есть). Только под `gate`.
    IReadOnlyList<EngineAction>? LiveEdit(KeyStroke stroke, LiveOptions options)
    {
        // Enter раскладка не отдаёт (служебная клавиша) — для правки на лету это конец строки
        var ch = stroke.KeyCode == VK_RETURN ? "\r" : layouts.Pair.Layout(engine.CurrentLanguage).Character(stroke);
        // стрелки, Home/End, мёртвые клавиши, F-клавиши — курсор ушёл или символ неизвестен: память не верна
        if (ch is null || TextElements.Count(ch) != 1 || (ch[0] < 0x20 && ch != "\r" && ch != "\t"))
        {
            live.Reset();
            return null;
        }
        if (live.Typed(ch, engine.CurrentLanguage, options) is not { } edit) return null;
        // длина текста изменилась (… вместо ...) — память свитчера больше не совпадает с экраном
        if (live.LastEditChangedLength) engine.Reset();
        // Enter: правку печатаем без него, а сам Enter жмём настоящей клавишей — иначе мессенджер не отправит
        if (ch == "\r" && edit.Insert.EndsWith('\r'))
            return [new EngineAction.Delete(edit.Delete), new EngineAction.Insert(edit.Insert[..^1]), new EngineAction.PressKey((ushort)VK_RETURN)];
        return [new EngineAction.Delete(edit.Delete), new EngineAction.Insert(edit.Insert)];
    }

    public void OnMouseDown()
    {
        OtherInputForTaps();
        lock (gate) { engine.Reset(); live.Reset(); }
    }

    public void OnForegroundChanged()
    {
        ResyncModifiers();
        // отпускание проглоченной клавиши могло потеряться — иначе однажды проглотим настоящее отпускание
        swallowed.Clear();
        lock (gate) { engine.Reset(); live.Reset(); }
        guard.Invalidate();
        poke.Set(); // раскладка в новом окне может быть другой
    }

    public void OnFocusChanged() => guard.Invalidate();

    /// Настройки поменяли в окне: слова «Не переключать» — сразу, сочетание — на потоке перехвата.
    public void SettingsChanged()
    {
        lock (gate) engine.Judge.Learned = new HashSet<string>(store.Current.LearnedWords);
        hotkeyChanged = true;
    }

    enum Fired { None, Convert, Correct }

    /// Детекторы двойного нажатия — из настроек. Только поток перехвата (и конструктор).
    void LoadHotkeys()
    {
        hasTap = store.Current.HotkeyConvert is Hotkey.DoubleTap;
        if (store.Current.HotkeyConvert is Hotkey.DoubleTap d) tap = new DoubleTapDetector(d.Key);
        hasCorrectTap = store.Current.HotkeyCorrect is Hotkey.DoubleTap;
        if (store.Current.HotkeyCorrect is Hotkey.DoubleTap dc) correctTap = new DoubleTapDetector(dc.Key);
    }

    void OtherInputForTaps()
    {
        if (hasTap) tap.OtherInput();
        if (hasCorrectTap) correctTap.OtherInput();
    }

    /// Какое сочетание сработало на этом модификаторе.
    Fired Modifier(in HookKey k)
    {
        if (hotkeyChanged)
        {
            hotkeyChanged = false;
            LoadHotkeys();
        }
        var now = Environment.TickCount64 / 1000.0;
        var fired = Fired.None;
        if (hasTap && Step(ref tap, k, now)) fired = Fired.Convert;
        if (hasCorrectTap && Step(ref correctTap, k, now)) fired = Fired.Correct;
        return fired;
    }

    /// Один детектор: своя клавиша — нажатие/отпускание, чужой модификатор — «другой ввод».
    static bool Step(ref DoubleTapDetector d, in HookKey k, double now)
    {
        if (k.Vk != d.Key.Vk())
        {
            d.OtherInput();
            return false;
        }
        if (k.Down)
        {
            d.KeyDown(now);
            return false;
        }
        return d.KeyUp(now);
    }

    /// «Исправить выделенный текст»: типографика выделения. В игре и в поле пароля молчим.
    void CorrectHotkey()
    {
        if (!guard.ManualAllowed()) return;
        if (!SelectionPolicy.MayCopy(false, guard.ExeName, guard.WindowClass, false)) return;
        lock (gate) { engine.Reset(); live.Reset(); }
        Enqueue(new FormatSelection());
    }

    /// Отпускание модификатора уже ушло в систему (или в очередь) — теперь исправление.
    void DoubleTap()
    {
        // в игре (на весь экран или «тихой») и в поле пароля молчим; в исключённом приложении — можно, человек попросил сам
        if (!guard.ManualAllowed()) return;
        IReadOnlyList<EngineAction> actions = [];
        bool readSelection;
        lock (gate)
        {
            // откат важнее выделения; есть откат или свежий набор — выделения быть не может, Ctrl+C не нужен
            // (в VS Code Ctrl+C без выделения копирует всю строку)
            var nothingTyped = !engine.CanUndo && !engine.HasTypedText;
            // в терминалах и редакторах (исключения), в консоли, в Excel, на третьей раскладке Ctrl+C вслепую не шлём
            readSelection = nothingTyped
                && SelectionPolicy.MayCopy(guard.InExcludedApp, guard.ExeName, guard.WindowClass, engine.IsForeignLayout);
            if (!nothingTyped)
            {
                actions = engine.HandleDoubleShift(null);
                if (HasSwitch(actions)) lastSwitchTick = Environment.TickCount64;
            }
        }
        // буфер обмена и ожидание Ctrl+C — на исполнителе: поток перехвата ждать не должен
        if (readSelection) Enqueue(new ConvertSelection());
        else if (actions.Count > 0) Enqueue(new Run(actions));
    }

    /// Очередь пуста — пропускаем как есть; иначе нажатие встаёт в очередь за нашими действиями.
    bool PassOrQueue(in HookKey k)
    {
        if (Volatile.Read(ref pending) == 0) return false;
        Enqueue(new Replay(k));
        return true;
    }

    void Enqueue(Work w)
    {
        if (queue.IsAddingCompleted) return;
        Interlocked.Increment(ref pending);
        queue.Add(w);
    }

    static bool HasSwitch(IReadOnlyList<EngineAction> actions)
    {
        for (var i = 0; i < actions.Count; i++)
            if (actions[i] is EngineAction.SwitchLayout) return true;
        return false;
    }

    void MarkSwitch()
    {
        lock (gate) lastSwitchTick = Environment.TickCount64;
    }

    bool AnyModifierHeld() => lShift || rShift || lCtrl || rCtrl || lAlt || rAlt || lWin || rWin;

    static bool IsModifierVk(uint vk) => vk is >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;

    /// Состояние модификаторов — из системы. Пока очередь не пуста, не трогаем: проглоченные нами
    /// нажатия система ещё не видела, она соврёт.
    void ResyncModifiers()
    {
        if (Volatile.Read(ref pending) != 0) return;
        static bool Down(int vk) => GetAsyncKeyState(vk) < 0;
        lShift = Down(0xA0); rShift = Down(0xA1);
        lCtrl = Down(0xA2); rCtrl = Down(0xA3);
        lAlt = Down(0xA4); rAlt = Down(0xA5);
        lWin = Down(0x5B); rWin = Down(0x5C);
    }

    void TrackModifier(uint vk, bool down)
    {
        switch (vk)
        {
            case 0xA0: lShift = down; break;
            case 0xA1: rShift = down; break;
            case 0xA2: lCtrl = down; break;
            case 0xA3: rCtrl = down; break;
            case 0xA4: lAlt = down; break;
            case 0xA5: rAlt = down; break;
            case 0x5B: lWin = down; break;
            case 0x5C: rWin = down; break;
        }
    }

    // ---- Исполнитель

    void WorkLoop()
    {
        foreach (var w in queue.GetConsumingEnumerable())
        {
            var delivered = true;
            try
            {
                switch (w)
                {
                    case Run r:
                        delivered = sender.Perform(r.Actions);
                        break;
                    case Replay { Key: var k }:
                        delivered = sender.Replay((ushort)k.Vk, (ushort)k.Scan, (k.Flags & LLKHF_EXTENDED) != 0, !k.Down);
                        break;
                    case ConvertSelection:
                        delivered = ConvertSelectionNow();
                        break;
                    case FormatSelection:
                        delivered = FormatSelectionNow();
                        break;
                }
                // система приняла не всё (UIPI, блокировка ввода) — экран и память движка разошлись
                if (!delivered) lock (gate) engine.Reset();
            }
            catch
            {
                // не вышло — следующее действие всё равно выполняем
            }
            finally
            {
                Interlocked.Decrement(ref pending);
            }
        }
    }

    /// Только исполнитель. Выделенный текст из чужого окна — в журнал ни слова.
    bool ConvertSelectionNow()
    {
        var text = selection.Copy();
        if (text is null) return true; // выделения нет — делать нечего
        IReadOnlyList<EngineAction> actions;
        lock (gate)
        {
            actions = engine.HandleDoubleShift(text);
            if (HasSwitch(actions)) lastSwitchTick = Environment.TickCount64;
        }
        var ok = true;
        var pasted = false;
        foreach (var a in actions)
        {
            if (a is not EngineAction.ReplaceSelection r) continue;
            ok &= selection.Paste(r.Text);
            pasted = true;
        }
        if (!pasted) selection.Restore();
        // смена раскладки — после вставки, как велел движок
        return sender.Perform(actions) && ok;
    }

    /// Только исполнитель. Типографика выделенного текста; править нечего — буфер возвращаем как был.
    bool FormatSelectionNow()
    {
        var text = selection.Copy();
        if (text is null) return true;
        var formatted = Typographer.Format(text, final: true, hint: engine.CurrentLanguage);
        if (formatted == text)
        {
            selection.Restore();
            return true;
        }
        return selection.Paste(formatted);
    }

    // ---- Слежение за раскладкой

    void Watch()
    {
        while (!stopping)
        {
            poke.WaitOne(PollMs);
            if (stopping) break;
            try { Poll(); }
            catch { }
        }
    }

    void Poll()
    {
        var hkl = SystemLayouts.Current();
        if (hkl == 0) return;
        var language = SystemLayouts.LanguageOf(hkl);
        // другая раскладка того же языка — таблицы снимаем вне замка, это небыстро
        if (language is { } l && layouts.Adopt(l, hkl) is { } pair)
            lock (gate) engine.UpdateLayouts(pair);

        var changed = hkl != lastHkl;
        lastHkl = hkl;
        if (changed)
        {
            // наша же смена — эхо, движок её уже знает и ничего не сбросит
            lock (gate) engine.ExternalLayoutChange(language);
            return;
        }
        // окно могло не послушаться нашей просьбы сменить раскладку — сверяемся, выждав
        if (language is { } actual)
        {
            lock (gate)
            {
                if (Environment.TickCount64 - lastSwitchTick > SwitchGraceMs && engine.CurrentLanguage != actual)
                    engine.ExternalLayoutChange(actual);
            }
        }
    }
}
