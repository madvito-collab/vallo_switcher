using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Vallo.Core;

namespace Vallo.App;

/// Окно настроек: та же страница, что на Маке (settings-ui/ → Settings/index.html рядом с exe), во встроенном Edge (WebView2).
/// Страница ничего не хранит: показывает настройки свитчера и шлёт изменения. Протокол и состояние — Vallo.Core.SettingsPage.
/// Живёт на потоке трея (его цикл сообщений — Application.Run).
sealed class SettingsWindow : IDisposable
{
    const string Host = "settings.vallo";

    readonly SettingsStore store;
    /// Настройки поменялись — свитчеру перечитать (сочетание, слова).
    readonly Action changed;
    Form? form;
    WebView2? web;
    bool ready;
    string? pendingPage;

    /// Обновления (раздел в «О программе»). Нет — свитчер не из установщика.
    public Updater? Updater { get; set; }
    /// Свитчер: запись своего сочетания идёт через его перехват клавиатуры. null — свитчер ещё не запущен.
    public Func<SwitcherService?>? Switcher { get; set; }
    /// Идёт запись своего сочетания: "convert" или "correct".
    string? recording;
    /// Записанное сочетание не подошло (занято другим действием).
    HotkeyError? hotkeyError;

    public SettingsWindow(SettingsStore store, Action changed)
    {
        this.store = store;
        this.changed = changed;
    }

    /// Показать окно (на нужной странице: "help", "about"…). Только с потока трея.
    public void Show(string? page = null)
    {
        pendingPage = page;
        if (form is null || form.IsDisposed) form = Create();
        if (!form.Visible) form.Show();
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        form.Activate();
        Push();
    }

    Form Create()
    {
        var f = new Form
        {
            Text = "Настройки Vallo Switcher",
            StartPosition = FormStartPosition.CenterScreen,
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(900, 640),
            MinimumSize = new Size(760, 480),
            ShowInTaskbar = true,
            BackColor = SystemColors.Window,
        };
        var icon = Path.Combine(AppContext.BaseDirectory, "Icons", "ValloSwitcher.ico");
        if (File.Exists(icon)) f.Icon = new Icon(icon);

        web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
        f.Controls.Add(web);
        f.FormClosed += (_, _) =>
        {
            // закрыли — освобождаем Edge целиком: свитчер весь день в фоне, память ему не нужна
            StopRecording();
            hotkeyError = null;
            ready = false;
            web?.Dispose();
            web = null;
            form = null;
        };
        f.Load += async (_, _) => await StartWeb();
        // заголовок окна — в тон теме Windows (страница сама следует теме). Ставим и при создании окна,
        // и когда оно уже на экране: иначе Windows иногда оставляет шапку светлой
        f.HandleCreated += (_, _) => ThemeTitleBar(f.Handle);
        f.Shown += (_, _) => ThemeTitleBar(f.Handle);
        // тему переключили, пока окно открыто
        Microsoft.Win32.UserPreferenceChangedEventHandler themeChanged = (_, e) =>
        {
            if (e.Category == Microsoft.Win32.UserPreferenceCategory.General && f.IsHandleCreated)
                f.BeginInvoke(() => ThemeTitleBar(f.Handle));
        };
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += themeChanged;
        f.FormClosed += (_, _) => Microsoft.Win32.SystemEvents.UserPreferenceChanged -= themeChanged;
        return f;
    }

    async Task StartWeb()
    {
        if (web is null) return;
        try
        {
            // своя папка данных Edge: рядом с exe писать нельзя (Program Files), в %LOCALAPPDATA% — можно
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vallo Switcher", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: data);
            await web.EnsureCoreWebView2Async(env);
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or COMException or InvalidOperationException)
        {
            // Edge WebView2 нет (бывает на урезанных сборках Windows 10) — объясняем и ведём на установку
            ShowNoRuntime();
            return;
        }
        var core = web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "Settings"),
            CoreWebView2HostResourceAccessKind.DenyCors);
        // ссылки — в браузер, не внутрь окна
        core.NewWindowRequested += (_, e) => { e.Handled = true; Open(e.Uri); };
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) { e.Cancel = true; Open(e.Uri); }
        };
        core.WebMessageReceived += (_, e) => Receive(e.WebMessageAsJson);
        core.Navigate($"https://{Host}/index.html");
    }

    void ShowNoRuntime()
    {
        if (form is null) return;
        form.Controls.Clear();
        var link = new LinkLabel
        {
            Text = "Для окна настроек нужен Microsoft Edge WebView2. Скачать с сайта Microsoft",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        link.LinkClicked += (_, _) => Open("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
        form.Controls.Add(link);
    }

    // --- страница → свитчер ---

    void Receive(string json)
    {
        JsonElement m;
        try { m = JsonDocument.Parse(json).RootElement; }
        catch (JsonException) { return; }
        if (m.ValueKind != JsonValueKind.Object || !m.TryGetProperty("type", out var type)) return;
        switch (type.GetString())
        {
            case "ready":
                ready = true;
                break;
            case "set" when m.TryGetProperty("key", out var key) && m.TryGetProperty("value", out var value):
                Set(key.GetString() ?? "", value);
                break;
            case "action" when m.TryGetProperty("name", out var name):
                Act(name.GetString() ?? "", m.TryGetProperty("arg", out var arg) ? arg : default);
                break;
        }
        Push();
    }

    void Set(string key, JsonElement value)
    {
        if (key is "hotkey.convert" or "hotkey.correct")
        {
            hotkeyError = null;
            StopRecording();
            if (value.ValueKind == JsonValueKind.String && value.GetString() == "record")
            {
                StartRecording(key == "hotkey.convert" ? "convert" : "correct");
                return;
            }
        }
        if (key == "launchAtLogin" && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            if (value.GetBoolean()) Autostart.Enable(); else Autostart.Disable();
            return;
        }
        store.Update(s => SettingsPage.Apply(s, key, value));
        DebugLog.Shared.Enabled = store.Current.DebugMode;
        changed();
    }

    void Act(string name, JsonElement arg)
    {
        switch (name)
        {
            case "pickApps":
                var list = arg.ValueKind == JsonValueKind.String ? arg.GetString() ?? "layout" : "layout";
                using (var dialog = new OpenFileDialog
                {
                    Title = list == "correct" ? "Приложения, где не исправлять текст" : "Приложения, где не переключать раскладку",
                    Filter = "Программы (*.exe)|*.exe",
                    Multiselect = true,
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                })
                {
                    if (dialog.ShowDialog(form) == DialogResult.OK)
                        store.Update(s => SettingsPage.AddApps(s, dialog.FileNames, list));
                }
                break;
            case "addWord" when arg.ValueKind == JsonValueKind.Object && arg.TryGetProperty("word", out var word):
                store.Update(s => SettingsPage.AddWord(s, word.GetString() ?? ""));
                changed();
                break;
            case "emojiPanel":
                // системная панель смайликов Windows (Win + .) — выбранный встанет в поле «Другой смайлик»
                keybd_event(0x5B, 0, 0, 0);
                keybd_event(0xBE, 0, 0, 0);
                keybd_event(0xBE, 0, 2, 0);
                keybd_event(0x5B, 0, 2, 0);
                break;
            case "openLog":
                OpenLog();
                break;
            case "cancelRecording":
                StopRecording();
                break;
            case "checkUpdates":
                Updater?.CheckNow();
                break;
            case "installUpdate":
                Updater?.InstallNow();
                break;
            case "openGitHub":
                Open("https://github.com/madvito-collab/vallo_switcher/issues");
                break;
            case "openFullHelp":
                Open("https://github.com/madvito-collab/vallo_switcher#readme");
                break;
        }
    }

    /// «Своё сочетание…»: ждём нажатие через перехват свитчера. Нажатие приходит с потока перехвата — в окно через BeginInvoke.
    void StartRecording(string action)
    {
        if (Switcher?.Invoke() is not { } switcher || form is null) return;
        recording = action;
        var f = form;
        switcher.StartRecording(combo =>
        {
            if (f.IsDisposed) return;
            f.BeginInvoke(() =>
            {
                if (recording != action) return;
                recording = null;
                if (combo is not null)
                {
                    HotkeyError? error = null;
                    store.Update(s =>
                    {
                        var (next, e) = SettingsPage.SetRecorded(s, action, combo);
                        error = e;
                        return next;
                    });
                    hotkeyError = error;
                    changed();
                }
                Push();
            });
        });
    }

    /// Журнал — в Блокноте. Пустой (только включили) — создаём, чтобы Блокнот не спрашивал про новый файл.
    static void OpenLog()
    {
        var log = DebugLog.Shared;
        log.Write("открыт журнал");
        log.Flush();
        try
        {
            if (!File.Exists(log.FilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(log.FilePath)!);
                File.WriteAllText(log.FilePath, "");
            }
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{log.FilePath}\"") { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
    }

    void StopRecording()
    {
        if (recording is null) return;
        recording = null;
        Switcher?.Invoke()?.StopRecording();
    }

    // --- свитчер → страница ---

    /// Что-то поменялось не со страницы (сходили за обновлениями) — показать. На потоке трея.
    public void Refresh() => Push();

    void Push()
    {
        if (!ready || web?.CoreWebView2 is null) return;
        var version = Updater.Short(Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "");
        var state = SettingsPage.State(store.Current, Autostart.IsEnabled, version, pendingPage, AppName,
                                       updates: Updater?.Supported == true, recording: recording, hotkeyError: hotkeyError,
                                       update: Updater is { } u ? Updates.PageState(u.Activity, u.Pending) : null);
        pendingPage = null;
        _ = web.CoreWebView2.ExecuteScriptAsync($"window.vallo && window.vallo.receive({state.ToJsonString()})");
    }

    /// «code.exe» → «Code». Имени программы в списке нет — только exe, его и показываем по-человечески.
    static string AppName(string exe)
    {
        var name = Path.GetFileNameWithoutExtension(exe);
        return name.Length == 0 ? exe : char.ToUpperInvariant(name[0]) + name[1..];
    }

    [DllImport("user32.dll")]
    static extern void keybd_event(byte vk, byte scan, uint flags, nuint extraInfo);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    /// Тёмная или светлая шапка окна — как приложения в теме Windows.
    static void ThemeTitleBar(nint hwnd)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var dark = key?.GetValue("AppsUseLightTheme") is int v && v == 0 ? 1 : 0;
        // 20 — DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 11 и новые Windows 10); 19 — то же в Windows 10 до 20H1
        if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
        // перерисовать рамку сейчас, не дожидаясь, пока окно потеряет и вернёт фокус
        const uint NoSizeNoMoveNoZOrderNoActivateFrameChanged = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020;
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, NoSizeNoMoveNoZOrderNoActivateFrameChanged);
    }

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    public void Dispose()
    {
        form?.Close();
        form?.Dispose();
    }
}
