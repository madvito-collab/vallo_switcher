using System.Runtime.InteropServices;
using Microsoft.Win32;
using Vallo.Core;
using static Vallo.App.Native;

namespace Vallo.App;

/// Значок «ё» в трее и его меню. Живёт на невидимом окне верхнего уровня:
/// окну «только для сообщений» не приходят широковещательные WM_SETTINGCHANGE и TaskbarCreated.
sealed unsafe class Tray : IDisposable
{
    const string ClassName = "ValloSwitcherTray";
    const uint CallbackMessage = WM_APP + 1;
    const uint IconId = 1;
    const int CmdAutoSwitch = 1, CmdQuit = 2;

    static Tray? current; // окно одно — оконной процедуре нужен доступ к объекту

    readonly SettingsStore store;
    readonly string iconsDir;
    readonly uint taskbarCreated;
    nint hwnd;
    nint hIcon;
    string? iconFile;
    bool added;

    public Tray(SettingsStore store, string iconsDir)
    {
        this.store = store;
        this.iconsDir = iconsDir;
        current = this;
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        var hInstance = GetModuleHandle(null);
        fixed (char* cls = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = hInstance,
                lpszClassName = cls,
            };
            if (RegisterClassEx(in wc) == 0)
                throw new InvalidOperationException($"RegisterClassEx: {Marshal.GetLastPInvokeError()}");
        }
        hwnd = CreateWindowEx(WS_EX_TOOLWINDOW, ClassName, "Vallo Switcher", WS_POPUP,
            0, 0, 0, 0, 0, 0, hInstance, 0);
        if (hwnd == 0)
            throw new InvalidOperationException($"CreateWindowEx: {Marshal.GetLastPInvokeError()}");

        AddIcon();
    }

    /// Цикл сообщений до «Завершить».
    public int Run()
    {
        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(in msg);
            DispatchMessage(in msg);
        }
        return 0;
    }

    public void Dispose()
    {
        RemoveIcon();
        if (hwnd != 0) DestroyWindow(hwnd);
        hwnd = 0;
        if (hIcon != 0) DestroyIcon(hIcon);
        hIcon = 0;
        current = null;
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        // исключение из [UnmanagedCallersOnly] роняет процесс
        try
        {
            var t = current;
            if (t is not null && t.Handle(hwnd, msg, wParam, lParam, out var result)) return result;
        }
        catch { }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    bool Handle(nint hwnd, uint msg, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (msg == CallbackMessage)
        {
            // NOTIFYICON_VERSION_4: событие — в младшем слове lParam, точка — в wParam
            var ev = (uint)(ushort)LoWord(lParam);
            if (ev is WM_CONTEXTMENU or NIN_SELECT or NIN_KEYSELECT)
                ShowMenu(LoWord((nint)wParam), HiWord((nint)wParam));
            return true;
        }
        if (msg == taskbarCreated && taskbarCreated != 0)
        {
            // проводник перезапустился (или сменился DPI) — значок надо добавить заново
            added = false;
            iconFile = null;
            AddIcon();
            return true;
        }
        switch (msg)
        {
            case WM_SETTINGCHANGE:
                if (lParam != 0 && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet") RefreshIcon();
                return false;
            case WM_DPICHANGED:
            case WM_DISPLAYCHANGE:
                RefreshIcon();
                return false;
            case WM_CLOSE:
            case WM_ENDSESSION when wParam != 0:
                Quit();
                return true;
            case WM_DESTROY:
                PostQuitMessage(0);
                return true;
        }
        return false;
    }

    void ShowMenu(int x, int y)
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            var autoFlags = MF_STRING | (store.Current.AutoSwitchEnabled ? MF_CHECKED : 0);
            AppendMenu(menu, autoFlags, CmdAutoSwitch, "Автопереключение");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CmdQuit, "Завершить Vallo Switcher");

            var align = GetSystemMetrics(SM_MENUDROPALIGNMENT) != 0 ? TPM_RIGHTALIGN : TPM_LEFTALIGN;
            // без SetForegroundWindow меню не закроется по клику мимо; WM_NULL — по документации TrackPopupMenu
            SetForegroundWindow(hwnd);
            var cmd = TrackPopupMenuEx(menu, align | TPM_BOTTOMALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD, x, y, hwnd, 0);
            PostMessage(hwnd, WM_NULL, 0, 0);

            switch (cmd)
            {
                case CmdAutoSwitch:
                    store.Update(s => s with { AutoSwitchEnabled = !s.AutoSwitchEnabled });
                    break;
                case CmdQuit:
                    Quit();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    void Quit()
    {
        RemoveIcon();
        if (hwnd != 0)
        {
            var h = hwnd;
            hwnd = 0;
            DestroyWindow(h); // → WM_DESTROY → PostQuitMessage
        }
    }

    // --- значок ---

    void AddIcon()
    {
        LoadIconForCurrentTheme();
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        added = Shell_NotifyIcon(NIM_ADD, in data);
        if (!added) return;
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, in data);
    }

    void RefreshIcon()
    {
        if (!added) { AddIcon(); return; }
        var old = iconFile;
        LoadIconForCurrentTheme();
        if (iconFile == old) return;
        var data = NewData(NIF_ICON);
        Shell_NotifyIcon(NIM_MODIFY, in data);
    }

    void RemoveIcon()
    {
        if (!added || hwnd == 0) return;
        var data = NewData(0);
        Shell_NotifyIcon(NIM_DELETE, in data);
        added = false;
    }

    NOTIFYICONDATAW NewData(uint flags)
    {
        var d = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = hwnd,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = hIcon,
        };
        "Vallo Switcher".AsSpan().CopyTo(new Span<char>(d.szTip, 127));
        return d;
    }

    /// Ставит hIcon под тему панели задач и её DPI; файл не сменился — ничего не делает.
    void LoadIconForCurrentTheme()
    {
        var dpi = TaskbarDpi();
        var file = TrayIconChoice.FileName(TaskbarIsLight(), dpi);
        if (file == iconFile && hIcon != 0) return;

        var size = (int)Math.Round(16.0 * dpi / 96);
        var icon = IconFromPng(Path.Combine(iconsDir, file));
        if (icon == 0) icon = LoadImage(GetModuleHandle(null), IDI_APP, IMAGE_ICON, size, size, LR_DEFAULTCOLOR);
        if (icon == 0) return;

        if (hIcon != 0) DestroyIcon(hIcon);
        hIcon = icon;
        iconFile = file;
    }

    /// PNG прямо в HICON: CreateIconFromResourceEx понимает PNG-кадры значков (Vista+).
    static nint IconFromPng(string path)
    {
        byte[] png;
        try { png = File.ReadAllBytes(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
        if (png.Length < 24) return 0;
        // ширина и высота — из заголовка IHDR (big-endian, байты 16..23)
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        fixed (byte* p = png)
            return CreateIconFromResourceEx(p, (uint)png.Length, true, 0x00030000, w, h, LR_DEFAULTCOLOR);
    }

    static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
    }

    // DPI самой панели задач (главный монитор); не нашли — DPI своего окна, потом 96.
    int TaskbarDpi()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        var dpi = taskbar != 0 ? GetDpiForWindow(taskbar) : 0;
        if (dpi == 0 && hwnd != 0) dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 96 : (int)dpi;
    }
}
