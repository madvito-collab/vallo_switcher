using Vallo.Core;
using static Vallo.App.Native;

namespace Vallo.App;

/// «Руки»: стирают, печатают, жмут клавиши, переключают раскладку. Только поток-исполнитель.
/// Все события помечены HookFilter.OurMarker — перехват их пропускает.
/// `switchPosted` — сразу после просьбы окну сменить раскладку.
sealed unsafe class InputSender(SystemLayouts layouts, SettingsStore store, Action switchPosted)
{
    const ushort VK_BACK = 0x08;

    readonly List<INPUT> batch = new();
    /// Все ли события текущего Perform/Replay система приняла.
    bool delivered;

    /// false — SendInput принял не всё: экран больше не совпадает с памятью движка.
    public bool Perform(IReadOnlyList<EngineAction> actions)
    {
        delivered = true;
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
                    // замену выделения через буфер обмена делает SelectionReader
                    break;
            }
        }
        Flush();
        return delivered;
    }

    /// Повторить проглоченное, пока мы работали, нажатие — как было.
    public bool Replay(ushort vk, ushort scan, bool extended, bool up)
    {
        delivered = true;
        Add(vk, scan, (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0));
        Flush();
        return delivered;
    }

    /// Сочетание вроде Ctrl+C: модификатор вниз, клавиша, модификатор вверх — одной пачкой.
    public bool Shortcut(ushort modifier, ushort vk)
    {
        delivered = true;
        var scan = (ushort)MapVirtualKey(modifier, MAPVK_VK_TO_VSC);
        Add(modifier, scan, 0);
        Key(vk);
        Add(modifier, scan, KEYEVENTF_KEYUP);
        Flush();
        return delivered;
    }

    void Key(ushort vk)
    {
        var scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        Add(vk, scan, 0);
        Add(vk, scan, KEYEVENTF_KEYUP);
    }

    /// Символами Юникода (VK_PACKET) — от раскладки не зависит. Смайлик — две половины (суррогатная пара):
    /// каждая нажимается и отпускается по очереди. Порядок «обе вниз, потом обе вверх» Телеграм (Qt) не понимает.
    void Text(string s)
    {
        foreach (var c in s)
        {
            Add(0, c, KEYEVENTF_UNICODE);
            Add(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
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
        uint sent;
        fixed (INPUT* p = arr) sent = SendInput((uint)arr.Length, p, sizeof(INPUT));
        // TODO(задача 11): в журнал отладки — sent из arr.Length (только числа, без текста)
        if (sent != arr.Length) delivered = false;
    }

    void SwitchLayout(Language l)
    {
        var hkl = layouts.Hkl(l);
        var hwnd = GetForegroundWindow();
        if (hkl == 0 || hwnd == 0) return;
        PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, 0, hkl);
        switchPosted();
    }

    void Learn(string word) => store.Update(s =>
        s.LearnedWords.Contains(word) ? s : s with { LearnedWords = [.. s.LearnedWords, word] });
}
