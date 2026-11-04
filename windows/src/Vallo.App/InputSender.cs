using Vallo.Core;
using static Vallo.App.Native;

namespace Vallo.App;

/// «Руки»: стирают, печатают, жмут клавиши, переключают раскладку. Только поток-исполнитель.
/// Все события помечены HookFilter.OurMarker — перехват их пропускает.
sealed unsafe class InputSender(SystemLayouts layouts, SettingsStore store)
{
    const ushort VK_BACK = 0x08;

    /// Когда последний раз просили сменить раскладку (Environment.TickCount64).
    public long LastSwitchTick;

    readonly List<INPUT> batch = new();

    public void Perform(IReadOnlyList<EngineAction> actions)
    {
        foreach (var action in actions)
        {
            switch (action)
            {
                case EngineAction.Delete d:
                    for (var i = 0; i < d.N; i++) Key(VK_BACK);
                    break;
                case EngineAction.Insert ins:
                    Text(ins.Text);
                    break;
                case EngineAction.PressKey p:
                    Key(p.Vk);
                    break;
                case EngineAction.SwitchLayout s:
                    // сначала дошлём набранное, потом — просьбу окну сменить раскладку
                    Flush();
                    SwitchLayout(s.L);
                    break;
                case EngineAction.Learn l:
                    Flush();
                    Learn(l.Word);
                    break;
                case EngineAction.ReplaceSelection:
                    // выделение — задача 10
                    break;
            }
        }
        Flush();
    }

    /// Повторить проглоченное, пока мы работали, нажатие — как было.
    public void Replay(ushort vk, ushort scan, bool extended, bool up)
    {
        Add(vk, scan, (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0));
        Flush();
    }

    void Key(ushort vk)
    {
        var scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        Add(vk, scan, 0);
        Add(vk, scan, KEYEVENTF_KEYUP);
    }

    /// Символами Юникода (VK_PACKET) — от раскладки не зависит. Суррогатная пара: обе половины вниз, потом обе вверх.
    void Text(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                Add(0, s[i], KEYEVENTF_UNICODE);
                Add(0, s[i + 1], KEYEVENTF_UNICODE);
                Add(0, s[i], KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
                Add(0, s[i + 1], KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
                i++;
                continue;
            }
            Add(0, s[i], KEYEVENTF_UNICODE);
            Add(0, s[i], KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
        }
    }

    void Add(ushort vk, ushort scan, uint flags) => batch.Add(new INPUT
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags, dwExtraInfo = HookFilter.OurMarker },
        },
    });

    /// Одним SendInput: чужой ввод не вклинится посреди пачки.
    void Flush()
    {
        if (batch.Count == 0) return;
        var arr = batch.ToArray();
        batch.Clear();
        fixed (INPUT* p = arr) SendInput((uint)arr.Length, p, sizeof(INPUT));
    }

    void SwitchLayout(Language l)
    {
        var hkl = layouts.Hkl(l);
        var hwnd = GetForegroundWindow();
        if (hkl == 0 || hwnd == 0) return;
        Interlocked.Exchange(ref LastSwitchTick, Environment.TickCount64);
        PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, 0, hkl);
    }

    void Learn(string word) => store.Update(s =>
        s.LearnedWords.Contains(word) ? s : s with { LearnedWords = [.. s.LearnedWords, word] });
}
