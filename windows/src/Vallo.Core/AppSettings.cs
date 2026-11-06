using System.Text.Json;

namespace Vallo.Core;

/// Настройки приложения; файл — %APPDATA%\Vallo Switcher\settings.json.
public sealed record AppSettings
{
    public static readonly IReadOnlyList<string> DefaultExcludedApps =
        ["windowsterminal.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "code.exe", "cursor.exe"];
    /// Игры в окне (Майнкрафт): молчим, как в полноэкранной игре, — даже двойной Shift.
    public static readonly IReadOnlyList<string> DefaultSilentApps = ["javaw.exe"];

    public bool AutoSwitchEnabled { get; init; } = true;
    /// Имена exe в нижнем регистре.
    public IReadOnlyList<string> ExcludedApps { get; init; } = DefaultExcludedApps;
    /// Имена exe в нижнем регистре.
    public IReadOnlyList<string> SilentApps { get; init; } = DefaultSilentApps;
    public IReadOnlyList<string> LearnedWords { get; init; } = [];
    public Hotkey HotkeyConvert { get; init; } = Hotkey.DefaultConvert;
    public bool DebugMode { get; init; }
    public bool FirstRunDone { get; init; }
    /// Скачивать и ставить новые версии самим (как на Маке); выключено — только сказать, что вышла.
    public bool AutoUpdate { get; init; } = true;
    /// Когда последний раз ходили за обновлениями.
    public DateTimeOffset? LastUpdateCheck { get; init; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vallo Switcher", "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// Нет файла или он битый — настройки по умолчанию.
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json);
            if (s is null) return new AppSettings();
            // явный null в файле — тоже умолчание
            var d = new AppSettings();
            return s with
            {
                ExcludedApps = s.ExcludedApps ?? d.ExcludedApps,
                SilentApps = s.SilentApps ?? d.SilentApps,
                LearnedWords = s.LearnedWords ?? d.LearnedWords,
                HotkeyConvert = s.HotkeyConvert ?? d.HotkeyConvert,
            };
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    /// Пишем во временный файл и подменяем — чтобы сбой посреди записи не оставил битый файл.
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
