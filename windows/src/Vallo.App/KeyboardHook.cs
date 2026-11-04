using System.Runtime.InteropServices;
using static Vallo.App.Native;

namespace Vallo.App;

/// Нажатие или отпускание клавиши из WH_KEYBOARD_LL.
readonly record struct HookKey(uint Vk, uint Scan, uint Flags, nuint ExtraInfo, bool Down);

/// Кому перехват отдаёт события. Всё вызывается на потоке перехвата и должно быть быстрым.
interface IHookSink
{
    /// true — проглотить нажатие.
    bool OnKey(in HookKey key);
    void OnMouseDown();
    void OnForegroundChanged();
    /// Фокус перешёл на другое поле (в том числе внутри окна). Только отметить — ничего не спрашивать.
    void OnFocusChanged();
}

/// «Уши»: WH_KEYBOARD_LL, WH_MOUSE_LL, смена активного окна и фокуса — на своём потоке с циклом сообщений.
sealed unsafe class KeyboardHook : IDisposable
{
    static IHookSink? sink; // перехват один — колбэкам нужен доступ к получателю

    /// Просьба потоку перехвата поставить перехват заново.
    const uint WM_REINSTALL = WM_APP + 2;

    readonly Thread thread;
    readonly ManualResetEventSlim started = new();
    uint threadId;
    bool ok;
    // --- только поток перехвата ---
    nint keyboard, mouse;

    public KeyboardHook(IHookSink receiver)
    {
        sink = receiver;
        thread = new Thread(Run) { IsBackground = true, Name = "Vallo hook", Priority = ThreadPriority.Highest };
    }

    /// false — Windows не дала поставить перехват.
    public bool Start()
    {
        thread.Start();
        started.Wait();
        return ok;
    }

    /// После сна и после разблокировки Windows могла молча снять перехват (не дождалась ответа) —
    /// ставим заново. Можно звать с любого потока: сама переустановка — на потоке перехвата.
    public void Reinstall()
    {
        if (threadId != 0) PostThreadMessage(threadId, WM_REINSTALL, 0, 0);
    }

    public void Dispose()
    {
        if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, 0, 0);
        if (thread.IsAlive) thread.Join(2000);
        sink = null;
        started.Dispose();
    }

    void Run()
    {
        threadId = GetCurrentThreadId();
        var module = GetModuleHandle(null);
        keyboard = SetWindowsHookEx(WH_KEYBOARD_LL, &KeyboardProc, module, 0);
        mouse = SetWindowsHookEx(WH_MOUSE_LL, &MouseProc, module, 0);
        var foreground = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, &ForegroundProc,
            0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        var focus = SetWinEventHook(EVENT_OBJECT_FOCUS, EVENT_OBJECT_FOCUS, 0, &FocusProc,
            0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        ok = keyboard != 0;
        started.Set();
        try
        {
            if (!ok) return;
            // колбэки перехвата приходят только пока поток крутит цикл сообщений
            while (GetMessage(out var msg, 0, 0, 0) > 0)
            {
                if (msg.hwnd == 0 && msg.message == WM_REINSTALL)
                {
                    Rehook(module);
                    continue;
                }
                TranslateMessage(in msg);
                DispatchMessage(in msg);
            }
        }
        finally
        {
            if (focus != 0) UnhookWinEvent(focus);
            if (foreground != 0) UnhookWinEvent(foreground);
            if (mouse != 0) UnhookWindowsHookEx(mouse);
            if (keyboard != 0) UnhookWindowsHookEx(keyboard);
        }
    }

    /// Снять и поставить заново. Новый не встал — оставляем старый: вдруг он ещё жив.
    void Rehook(nint module)
    {
        var k = SetWindowsHookEx(WH_KEYBOARD_LL, &KeyboardProc, module, 0);
        if (k != 0)
        {
            if (keyboard != 0) UnhookWindowsHookEx(keyboard);
            keyboard = k;
        }
        var m = SetWindowsHookEx(WH_MOUSE_LL, &MouseProc, module, 0);
        if (m != 0)
        {
            if (mouse != 0) UnhookWindowsHookEx(mouse);
            mouse = m;
        }
        // пока перехвата не было, нажатия и окна менялись без нас — начинаем с чистого листа
        try { sink?.OnForegroundChanged(); }
        catch { }
    }

    // Исключение из [UnmanagedCallersOnly] роняет процесс — ловим всё и пропускаем событие дальше.

    [UnmanagedCallersOnly]
    static nint KeyboardProc(int code, nuint wParam, nint lParam)
    {
        try
        {
            if (code >= 0 && sink is { } s)
            {
                var k = (KBDLLHOOKSTRUCT*)lParam;
                var down = wParam is WM_KEYDOWN or WM_SYSKEYDOWN;
                if (s.OnKey(new HookKey(k->vkCode, k->scanCode, k->flags, k->dwExtraInfo, down))) return 1;
            }
        }
        catch
        {
            // пропускаем клавишу; про набранное — ни слова
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    static nint MouseProc(int code, nuint wParam, nint lParam)
    {
        try
        {
            if (code >= 0 && wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN)
                sink?.OnMouseDown();
        }
        catch { }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    static void ForegroundProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint threadId, uint time)
    {
        try
        {
            sink?.OnForegroundChanged();
        }
        catch { }
    }

    [UnmanagedCallersOnly]
    static void FocusProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint threadId, uint time)
    {
        try
        {
            sink?.OnFocusChanged();
        }
        catch { }
    }
}
