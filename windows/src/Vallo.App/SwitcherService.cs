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
        sender = new InputSender(layouts, store);
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
        var judge = new Judge(
            en: Lexicon.Load(Path.Combine(data, "en.txt"), Language.En),
            ru: Lexicon.Load(Path.Combine(data, "ru.txt"), Language.Ru,
                extra: WordForms.Load(Path.Combine(data, "ru-modern.txt"))),
            learned: new HashSet<string>(store.Current.LearnedWords));
        var hkl = SystemLayouts.Current();
        var current = SystemLayouts.LanguageOf(hkl);
        var engine = new Engine(layouts.Pair, judge, current ?? Language.En)
        {
            AutoSwitchEnabled = store.Current.AutoSwitchEnabled,
        };
        if (current is null) engine.ExternalLayoutChange(null);

        var service = new SwitcherService(store, layouts, engine) { lastHkl = hkl };
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

    // ---- Перехват (быстро!)

    public bool OnKey(in HookKey k)
    {
        if (k.ExtraInfo == HookFilter.OurMarker) return false;
        TrackModifier(k.Vk, k.Down);
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
        lock (gate) actions = engine.HandleDoubleShift(null);
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
        if (w is Run r && r.Actions.Any(a => a is EngineAction.SwitchLayout))
            Interlocked.Exchange(ref sender.LastSwitchTick, Environment.TickCount64);
        if (queue.IsAddingCompleted) return;
        Interlocked.Increment(ref pending);
        queue.Add(w);
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
            try
            {
                switch (w)
                {
                    case Run r:
                        sender.Perform(r.Actions);
                        break;
                    case Replay { Key: var k }:
                        sender.Replay((ushort)k.Vk, (ushort)k.Scan, (k.Flags & LLKHF_EXTENDED) != 0, !k.Down);
                        break;
                }
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
        if (language is { } actual
            && Environment.TickCount64 - Interlocked.Read(ref sender.LastSwitchTick) > SwitchGraceMs)
        {
            lock (gate)
                if (engine.CurrentLanguage != actual) engine.ExternalLayoutChange(actual);
        }
    }
}
