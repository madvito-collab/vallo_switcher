using System.Collections.Concurrent;
using Vallo.Core;
using static Vallo.App.Native;

namespace Vallo.App;

/// Свитчер целиком: перехват → движок → очередь действий → исполнитель; плюс слежение за раскладкой.
///
/// Потоки:
/// - перехват (KeyboardHook): сортирует событие, спрашивает движок, решает «пропустить/проглотить», кладёт в очередь;
/// - исполнитель: SendInput и смена раскладки по очереди, строго по порядку;
/// - слежение: раскладка активного окна раз в 250 мс и сразу при смене окна.
/// Движок трогают все трое — только под `gate`.
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

    abstract record Work;
    sealed record Run(IReadOnlyList<EngineAction> Actions) : Work;
    sealed record Replay(HookKey Key) : Work;

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
    readonly bool hasTap;
    bool lShift, rShift, lCtrl, rCtrl, lAlt, rAlt, lWin, rWin;
    /// Клавиши, чьё нажатие проглотили, — отпускание глотаем тоже.
    readonly HashSet<uint> swallowed = new();

    SwitcherService(SettingsStore store, SystemLayouts layouts, Engine engine)
    {
        this.store = store;
        this.layouts = layouts;
        this.engine = engine;
        sender = new InputSender(layouts, store, MarkSwitch);
        if (store.Current.HotkeyConvert is Hotkey.DoubleTap d)
        {
            tap = new DoubleTapDetector(d.Key);
            hasTap = true;
        }
        hook = new KeyboardHook(this);
        worker = new Thread(WorkLoop) { IsBackground = true, Name = "Vallo input", Priority = ThreadPriority.AboveNormal };
        watcher = new Thread(Watch) { IsBackground = true, Name = "Vallo layouts" };
    }

    /// null — свитчер не запустить (нет EN/RU раскладки, нет словарей, не дали перехват). Трей живёт и без него.
    public static SwitcherService? Start(SettingsStore store)
    {
        var layouts = SystemLayouts.Create();
        if (layouts is null) return null;

        var data = Path.Combine(AppContext.BaseDirectory, "Data", "wordlists");
        var en = Lexicon.Load(Path.Combine(data, "en.txt"), Language.En);
        var ru = Lexicon.Load(Path.Combine(data, "ru.txt"), Language.Ru,
            extra: WordForms.Load(Path.Combine(data, "ru-modern.txt")));
        var judge = new Judge(en, ru, learned: new HashSet<string>(store.Current.LearnedWords));
        WarmUp(layouts.Pair, en, ru);
        var hkl = SystemLayouts.Current();
        var current = SystemLayouts.LanguageOf(hkl);
        var engine = new Engine(layouts.Pair, judge, current ?? Language.En)
        {
            AutoSwitchEnabled = store.Current.AutoSwitchEnabled,
        };
        if (current is null) engine.ExternalLayoutChange(null);

        var service = new SwitcherService(store, layouts, engine) { lastHkl = hkl };
        service.WarmUpHookPath();
        service.worker.Start();
        if (!service.hook.Start())
        {
            service.Dispose();
            return null;
        }
        service.watcher.Start();
        return service;
    }

    public void Dispose()
    {
        stopping = true;
        hook.Dispose();
        poke.Set();
        if (watcher.IsAlive) watcher.Join(1000);
        queue.CompleteAdding();
        if (worker.IsAlive) worker.Join(1000);
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
    }

    /// Свой код перехвата — скомпилировать заранее.
    void WarmUpHookPath()
    {
        foreach (var name in new[] { nameof(OnKey), nameof(PassOrQueue), nameof(Enqueue), nameof(Modifier), nameof(DoubleTap),
                     nameof(TrackModifier), nameof(ResyncModifiers), nameof(HasSwitch), nameof(OnMouseDown), nameof(OnForegroundChanged) })
        {
            var m = typeof(SwitcherService).GetMethod(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (m is not null) System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(m.MethodHandle);
        }
        HookFilter.Classify(0x41, 0, 0, false, false, false);
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
                if (hasTap) tap.OtherInput();
                if (k.Down) lock (gate) engine.Reset();
                // клавишу, чьё нажатие проглотили, отпустили уже с Ctrl/Alt — отпускание тоже глотаем
                if (!k.Down && swallowed.Remove(k.Vk)) return true;
                return PassOrQueue(k);
            case HookDecision.Modifier:
                var fired = Modifier(k);
                var result = PassOrQueue(k);
                if (fired) DoubleTap();
                return result;
        }

        // обычная клавиша
        if (!k.Down) return swallowed.Remove(k.Vk) || PassOrQueue(k);
        if (hasTap) tap.OtherInput();
        var stroke = new KeyStroke((ushort)k.Vk, lShift || rShift, (GetKeyState(VK_CAPITAL) & 1) != 0);
        EngineOutput output;
        lock (gate)
        {
            engine.AutoSwitchEnabled = store.Current.AutoSwitchEnabled;
            output = engine.HandleKey(new KeyEvent(stroke, HasCtrlOrAlt: false));
            if (output is EngineOutput.Consume c && HasSwitch(c.Actions)) lastSwitchTick = Environment.TickCount64;
        }
        if (output is not EngineOutput.Consume consume) return PassOrQueue(k);
        swallowed.Add(k.Vk);
        Enqueue(new Run(consume.Actions));
        return true;
    }

    public void OnMouseDown()
    {
        if (hasTap) tap.OtherInput();
        lock (gate) engine.Reset();
    }

    public void OnForegroundChanged()
    {
        ResyncModifiers();
        // отпускание проглоченной клавиши могло потеряться — иначе однажды проглотим настоящее отпускание
        swallowed.Clear();
        lock (gate) engine.Reset();
        poke.Set(); // раскладка в новом окне может быть другой
    }

    /// true — сработало двойное нажатие.
    bool Modifier(in HookKey k)
    {
        if (!hasTap) return false;
        if (k.Vk != tap.Key.Vk())
        {
            tap.OtherInput();
            return false;
        }
        var now = Environment.TickCount64 / 1000.0;
        if (k.Down)
        {
            tap.KeyDown(now);
            return false;
        }
        return tap.KeyUp(now);
    }

    /// Отпускание модификатора уже ушло в систему (или в очередь) — теперь исправление.
    void DoubleTap()
    {
        IReadOnlyList<EngineAction> actions;
        // выделение (задача 10) пока не читаем — только откат и перевод набранного
        lock (gate)
        {
            actions = engine.HandleDoubleShift(null);
            if (HasSwitch(actions)) lastSwitchTick = Environment.TickCount64;
        }
        if (actions.Count > 0) Enqueue(new Run(actions));
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
