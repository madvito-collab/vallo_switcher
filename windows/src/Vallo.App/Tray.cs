using System.Collections.Concurrent;
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
    /// Работа с других потоков (обновлятор) — выполнить на потоке трея.
    const uint PostedWork = WM_APP + 2;
    const uint IconId = 1;
    const int CmdAutoSwitch = 1, CmdQuit = 2, CmdSettings = 3, CmdHelp = 4, CmdAbout = 5, CmdUpdate = 6, CmdCheckUpdates = 7;

    static Tray? current; // окно одно — оконной процедуре нужен доступ к объекту

    readonly SettingsStore store;
    readonly string iconsDir;
    readonly uint taskbarCreated;
    nint hwnd;
    nint hIcon;
    string? iconFile;
    bool added;
    bool sessionNotify;
    /// Свитчер не запустился — первой строкой меню. Пишет поток запуска, читает поток трея.
    volatile string? problem;
    readonly ConcurrentQueue<Action> posted = new();
    /// Красная точка на значке: ждёт обновление.
    bool badge;
    /// Клик по последнему уведомлению ставит обновление.
    bool notifyInstalls;

    /// Проснулись после сна или разблокировали сеанс. Зовётся на потоке трея.
    public Action? Resumed { get; set; }
    /// Открыть окно настроек (null — главная страница, "help", "about"). На потоке трея.
    public Action<string?>? OpenSettings { get; set; }
    /// Автообновление: строка в меню, точка на значке. Нет — свитчер не из установщика.
    public Updater? Updater { get; set; }
    /// Перед выходом из свитчера (поставить скачанное обновление).
    public Action? BeforeQuit { get; set; }

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
        // разблокировку сеанса окну сообщают только по подписке; про сон (WM_POWERBROADCAST) — всем окнам верхнего уровня
        sessionNotify = WTSRegisterSessionNotification(hwnd, NOTIFY_FOR_THIS_SESSION);
    }

    /// Показать в меню, почему свитчер не работает. С любого потока.
    public void ShowProblem(string text) => problem = text;

    /// Убрать значок (перед перезапуском с новой версией). На потоке трея.
    public void Hide() => RemoveIcon();

    /// Выполнить на потоке трея. С любого потока.
    public void Post(Action work)
    {
        posted.Enqueue(work);
        if (hwnd != 0) PostMessage(hwnd, PostedWork, 0, 0);
    }

    /// Точка на значке, как на Маке: ждёт обновление. На потоке трея.
    public void ShowBadge(bool on)
    {
        if (badge == on) return;
        badge = on;
        iconFile = null;
        RefreshIcon();
    }

    /// Уведомление в углу экрана от значка. `installsOnClick` — клик по нему ставит обновление. На потоке трея.
    public void Notify(string title, string body, bool installsOnClick)
    {
        if (!added) return;
        notifyInstalls = installsOnClick;
        var data = NewData(NIF_INFO);
        data.dwInfoFlags = NIIF_INFO;
        title.AsSpan(0, Math.Min(title.Length, 63)).CopyTo(new Span<char>(data.szInfoTitle, 63));
        body.AsSpan(0, Math.Min(body.Length, 255)).CopyTo(new Span<char>(data.szInfo, 255));
        Shell_NotifyIcon(NIM_MODIFY, in data);
    }

    /// Цикл сообщений до «Завершить». Цикл WinForms: в нём живёт и окно настроек (клавиатура, async WebView2);
    /// WM_QUIT от PostQuitMessage его тоже завершает.
    public int Run()
    {
        System.Windows.Forms.Application.Run();
        return 0;
    }

    public void Dispose()
    {
        RemoveIcon();
        if (sessionNotify && hwnd != 0) WTSUnRegisterSessionNotification(hwnd);
        sessionNotify = false;
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
            else if (ev == NIN_BALLOONUSERCLICK && notifyInstalls)
                Updater?.InstallNow();
            return true;
        }
        if (msg == PostedWork)
        {
            while (posted.TryDequeue(out var work)) work();
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
            case WM_POWERBROADCAST when wParam == PBT_APMRESUMEAUTOMATIC:
            case WM_WTSSESSION_CHANGE when wParam == WTS_SESSION_UNLOCK:
                Resumed?.Invoke();
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
            if (problem is { } p)
            {
                AppendMenu(menu, MF_STRING | MF_GRAYED, 0, p);
                AppendMenu(menu, MF_SEPARATOR, 0, null);
            }
            // вышло обновление — первой строкой, как на Маке
            if (Updater?.Pending is { } pending)
            {
                AppendMenu(menu, MF_STRING, CmdUpdate, Updates.MenuTitle(pending));
                AppendMenu(menu, MF_SEPARATOR, 0, null);
            }
            var autoFlags = MF_STRING | (store.Current.AutoSwitchEnabled ? MF_CHECKED : 0);
            AppendMenu(menu, autoFlags, CmdAutoSwitch, "Автопереключение");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CmdSettings, "Настройки…");
            if (Updater?.Supported == true) AppendMenu(menu, MF_STRING, CmdCheckUpdates, "Проверить обновления…");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CmdHelp, "Справка");
            AppendMenu(menu, MF_STRING, CmdAbout, "О программе");
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
                case CmdSettings:
                    OpenSettings?.Invoke(null);
                    break;
                case CmdHelp:
                    OpenSettings?.Invoke("help");
                    break;
                case CmdAbout:
                    OpenSettings?.Invoke("about");
                    break;
                case CmdUpdate:
                    Updater?.InstallNow();
                    break;
                case CmdCheckUpdates:
                    Updater?.CheckNow();
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
        try { BeforeQuit?.Invoke(); } catch { }
        RemoveIcon();
        if (sessionNotify && hwnd != 0) WTSUnRegisterSessionNotification(hwnd);
        sessionNotify = false;
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
        var name = TrayIconChoice.FileName(TaskbarIsLight(), dpi);
        var file = badge ? name + "+badge" : name;
        if (file == iconFile && hIcon != 0) return;

        var size = (int)Math.Round(16.0 * dpi / 96);
        var path = Path.Combine(iconsDir, name);
        var icon = badge ? IconWithBadge(path) : 0;
        if (icon == 0) icon = IconFromPng(path);
        if (icon == 0) icon = LoadImage(GetModuleHandle(null), IDI_APP, IMAGE_ICON, size, size, LR_DEFAULTCOLOR);
        if (icon == 0) return;

        if (hIcon != 0) DestroyIcon(hIcon);
        hIcon = icon;
        iconFile = file;
    }

    /// «ё» с красной точкой в правом верхнем углу (как бейдж на Маке).
    static nint IconWithBadge(string path)
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(path);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var d = bmp.Width * 0.45f;
                using var red = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 59, 48));
                g.FillEllipse(red, bmp.Width - d, 0, d, d);
            }
            return bmp.GetHicon();
        }
        catch (Exception e) when (e is ArgumentException or IOException or System.Runtime.InteropServices.ExternalException) { return 0; }
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
