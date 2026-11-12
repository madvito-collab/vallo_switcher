using System.Text.Json;

namespace Vallo.Core;

/// Своя пара смайлика: скобки → смайлик.
public sealed record EmojiPair(string Trigger, string Emoji);

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
    /// Типографика на лету (тире, кавычки, неразрывные пробелы, телефоны, разряды).
    public bool TypographyLive { get; init; } = true;
    /// Опечатки на лету: «првет» → «привет» (как на Маке).
    public bool TyposLive { get; init; } = true;
    /// Буква «ё» на лету, где без вариантов: «еще» → «ещё».
    public bool YoLive { get; init; } = true;
    /// «Не исправлять слова»: опечатки их не трогают. Нормализованы (строчные, ё → е).
    /// Сюда же попадает слово, исправление которого отменили Backspace.
    public IReadOnlyList<string> TypoIgnoredWords { get; init; } = [];
    /// «Заглавная буква и точка» при исправлении выделенного (вместе с запятыми). Как на Маке — по умолчанию выключено.
    public bool Capitalize { get; init; }
    /// Сочетание «исправить выделенный текст» (типографика); null — выключено.
    public Hotkey? HotkeyCorrect { get; init; } = Hotkey.DefaultTypography;
    /// Где не работает исправление на лету (имена exe в нижнем регистре).
    public IReadOnlyList<string> TypographyExcludedApps { get; init; } = DefaultExcludedApps;
    /// Смайлики из скобок: "millennial", "zoomer", "custom" или "off".
    public string EmojiPreset { get; init; } = "millennial";
    /// Свой набор смайликов — в порядке, как на странице настроек.
    public IReadOnlyList<EmojiPair> EmojiCustom { get; init; } = [];
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
                TypoIgnoredWords = s.TypoIgnoredWords ?? d.TypoIgnoredWords,
                HotkeyConvert = s.HotkeyConvert ?? d.HotkeyConvert,
                TypographyExcludedApps = s.TypographyExcludedApps ?? d.TypographyExcludedApps,
                EmojiPreset = s.EmojiPreset ?? d.EmojiPreset,
                EmojiCustom = s.EmojiCustom ?? d.EmojiCustom,
            };
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    /// Какой набор смайликов включён на лету; null — выключено.
    public EmojiBrackets? EmojiBrackets() => EmojiPreset switch
    {
        "off" => null,
        "zoomer" => Core.EmojiBrackets.Zoomer,
        "custom" => new Core.EmojiBrackets(EmojiCustom.Where(p => p.Trigger.Length > 0)
            .GroupBy(p => p.Trigger).ToDictionary(g => g.Key, g => g.Last().Emoji)),
        _ => Core.EmojiBrackets.Millennial,
    };

    /// Пишем во временный файл и подменяем — чтобы сбой посреди записи не оставил битый файл.
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
