using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vallo.Core;

/// Окно настроек на Windows показывает ту же страницу, что и на Маке (settings-ui/ → Support/SettingsUI/index.html).
/// Здесь — что страница видит (состояние) и как её изменения ложатся в настройки.
/// Протокол и состояние — settings-ui/src/bridge.ts.
public static class SettingsPage
{
    /// Состояние для страницы. На Windows: раскладка и типографика со смайликами; опечаток, «ё», запятых, Figma — пока нет.
    /// `updates` — свитчер поставлен установщиком и умеет обновляться (из архива — нет).
    /// `recording` — идёт запись своего сочетания ("convert"/"correct"), `hotkeyError` — почему записанное не подошло.
    public static JsonObject State(AppSettings s, bool launchAtLogin, string version, string? page, Func<string, string> appName,
                                   bool updates = false, string? recording = null, HotkeyError? hotkeyError = null)
    {
        JsonArray Apps(IEnumerable<string> list)
        {
            var a = new JsonArray();
            foreach (var exe in list) a.Add(new JsonObject { ["id"] = exe, ["name"] = appName(exe) });
            return a;
        }
        var apps = Apps(s.ExcludedApps);
        var custom = new JsonArray();
        foreach (var p in s.EmojiCustom) custom.Add(new JsonObject { ["trigger"] = p.Trigger, ["emoji"] = p.Emoji });

        var options = new JsonArray();
        foreach (var key in Enum.GetValues<ModifierKey>())
            options.Add(new JsonObject { ["id"] = DoubleId(key), ["title"] = key.Title() });

        return new JsonObject
        {
            ["platform"] = "windows",
            ["version"] = version,
            ["page"] = page,
            ["general"] = new JsonObject { ["launchAtLogin"] = launchAtLogin, ["debugMode"] = s.DebugMode },
            ["attention"] = new JsonObject { ["modelMissing"] = false, ["macAutocorrect"] = false },
            ["layout"] = new JsonObject
            {
                ["autoSwitch"] = s.AutoSwitchEnabled,
                ["hotkey"] = HotkeyId(s.HotkeyConvert),
                ["hotkeyTitle"] = s.HotkeyConvert.Title,
                ["apps"] = apps,
                ["words"] = new JsonArray(s.LearnedWords.Select(w => (JsonNode?)w).ToArray()),
            },
            ["correct"] = new JsonObject
            {
                // опечатки, «ё» и запятые на Windows — следующая очередь: страница прячет их строки
                ["available"] = true, ["typosAvailable"] = false,
                ["hotkey"] = s.HotkeyCorrect is { } hc ? HotkeyId(hc) : "off", ["hotkeyTitle"] = s.HotkeyCorrect?.Title ?? "",
                ["capitalize"] = false, ["typography"] = s.TypographyLive, ["typos"] = false, ["yo"] = false,
                ["apps"] = Apps(s.TypographyExcludedApps), ["words"] = new JsonArray(),
                ["emoji"] = new JsonObject { ["preset"] = s.EmojiPreset, ["custom"] = custom },
            },
            ["hotkeyOptions"] = options,
            ["canRecordHotkey"] = true,
            ["recording"] = recording,
            ["hotkeyError"] = hotkeyError is { } he ? new JsonObject { ["action"] = he.Action, ["text"] = he.Text } : null,
            ["integrations"] = new JsonObject { ["figma"] = false, ["figmaPublished"] = false },
            ["about"] = new JsonObject
            {
                ["updates"] = updates, ["autoUpdate"] = s.AutoUpdate,
                ["lastCheck"] = s.LastUpdateCheck?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                ["model"] = new JsonObject { ["state"] = "notDownloaded", ["progress"] = 0, ["message"] = "", ["size"] = "", ["disk"] = "" },
            },
        };
    }

    /// Изменение со страницы. Незнакомый ключ или значение не того вида — настройки те же (тот же объект).
    public static AppSettings Apply(AppSettings s, string key, JsonElement value)
    {
        switch (key)
        {
            case "autoSwitch" when IsBool(value):
                return s with { AutoSwitchEnabled = value.GetBoolean() };
            case "debugMode" when IsBool(value):
                return s with { DebugMode = value.GetBoolean() };
            case "autoUpdate" when IsBool(value):
                return s with { AutoUpdate = value.GetBoolean() };
            // одно сочетание на два действия не даём: второе просто не сохранится
            case "hotkey.convert" when value.ValueKind == JsonValueKind.String:
                return ParseHotkey(value.GetString()) is { } h && h != s.HotkeyCorrect ? s with { HotkeyConvert = h } : s;
            case "hotkey.correct" when value.ValueKind == JsonValueKind.String:
                if (value.GetString() == "off") return s with { HotkeyCorrect = null };
                return ParseHotkey(value.GetString()) is { } hc && hc != s.HotkeyConvert ? s with { HotkeyCorrect = hc } : s;
            case "typography" when IsBool(value):
                return s with { TypographyLive = value.GetBoolean() };
            case "apps.correct" when Strings(value) is { } tapps:
                return s with { TypographyExcludedApps = tapps.Select(a => a.ToLowerInvariant()).Distinct().ToArray() };
            case "emoji.preset" when value.ValueKind == JsonValueKind.String
                                     && value.GetString() is "millennial" or "zoomer" or "custom" or "off":
                return s with { EmojiPreset = value.GetString()! };
            case "emoji.custom" when value.ValueKind == JsonValueKind.Array:
                return s with { EmojiCustom = value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(e => new EmojiPair(Str(e, "trigger"), Str(e, "emoji"))).ToArray() };
            case "apps.layout" when Strings(value) is { } apps:
                return s with { ExcludedApps = apps.Select(a => a.ToLowerInvariant()).Distinct().ToArray() };
            case "words.layout" when Strings(value) is { } words:
                return s with { LearnedWords = words };
            default:
                return s;
        }
    }

    /// Записанное своё сочетание для действия ("convert" — раскладка, "correct" — исправить текст).
    /// Уже занято другим действием — настройки те же и объяснение, как на Маке.
    public static (AppSettings Settings, HotkeyError? Error) SetRecorded(AppSettings s, string action, Hotkey.Combo combo)
    {
        if (action == "convert")
            return combo == s.HotkeyCorrect ? (s, new HotkeyError(action, "Это сочетание уже исправляет текст")) : (s with { HotkeyConvert = combo }, null);
        return combo == s.HotkeyConvert ? (s, new HotkeyError(action, "Это сочетание уже меняет раскладку")) : (s with { HotkeyCorrect = combo }, null);
    }

    /// Слово в «Не переключать», набранное руками.
    public static AppSettings AddWord(AppSettings s, string raw)
    {
        var w = Lexicon.Normalize(raw.Trim());
        if (w.Length == 0 || s.LearnedWords.Contains(w)) return s;
        return s with { LearnedWords = [.. s.LearnedWords, w] };
    }

    /// Имя exe из выбранного файла — так их хранит список исключений. `list` — "layout" или "correct".
    public static AppSettings AddApps(AppSettings s, IEnumerable<string> paths, string list = "layout")
    {
        var current = list == "correct" ? s.TypographyExcludedApps : s.ExcludedApps;
        // имя файла после последнего «\\» или «/» — одинаково на Windows и в тестах на Маке
        var added = paths.Select(p => p.Split('\\', '/')[^1].ToLowerInvariant())
            .Where(e => e.Length > 0 && !current.Contains(e)).Distinct().ToArray();
        if (added.Length == 0) return s;
        return list == "correct"
            ? s with { TypographyExcludedApps = [.. current, .. added] }
            : s with { ExcludedApps = [.. current, .. added] };
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static string DoubleId(ModifierKey key) => "double:" + char.ToLowerInvariant(key.ToString()[0]) + key.ToString()[1..];

    static string HotkeyId(Hotkey h) => h is Hotkey.DoubleTap d ? DoubleId(d.Key) : "custom";

    static Hotkey? ParseHotkey(string? id)
    {
        foreach (var key in Enum.GetValues<ModifierKey>())
            if (id == DoubleId(key)) return new Hotkey.DoubleTap(key);
        return null;
    }

    static bool IsBool(JsonElement v) => v.ValueKind is JsonValueKind.True or JsonValueKind.False;

    static string[]? Strings(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Array) return null;
        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) return null;
            list.Add(item.GetString()!);
        }
        return list.ToArray();
    }
}

/// Записанное сочетание не подошло: для какого действия ("convert"/"correct") и почему.
public sealed record HotkeyError(string Action, string Text);
