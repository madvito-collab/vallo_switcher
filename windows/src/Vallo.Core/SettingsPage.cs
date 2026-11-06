using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vallo.Core;

/// Окно настроек на Windows показывает ту же страницу, что и на Маке (settings-ui/ → Support/SettingsUI/index.html).
/// Здесь — что страница видит (состояние) и как её изменения ложатся в настройки.
/// Протокол и состояние — settings-ui/src/bridge.ts.
public static class SettingsPage
{
    /// Состояние для страницы. На Windows пока только раскладка: исправлений, Figma и модели нет.
    /// `updates` — свитчер поставлен установщиком и умеет обновляться (из архива — нет).
    public static JsonObject State(AppSettings s, bool launchAtLogin, string version, string? page, Func<string, string> appName,
                                   bool updates = false)
    {
        var apps = new JsonArray();
        foreach (var exe in s.ExcludedApps)
            apps.Add(new JsonObject { ["id"] = exe, ["name"] = appName(exe) });

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
                ["available"] = false, ["hotkey"] = "off", ["hotkeyTitle"] = "", ["capitalize"] = false,
                ["typography"] = false, ["typos"] = false, ["yo"] = false, ["apps"] = new JsonArray(), ["words"] = new JsonArray(),
                ["emoji"] = new JsonObject { ["preset"] = "off", ["custom"] = new JsonArray() },
            },
            ["hotkeyOptions"] = options,
            ["canRecordHotkey"] = false,
            ["recording"] = null,
            ["hotkeyError"] = null,
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
            case "hotkey.convert" when value.ValueKind == JsonValueKind.String:
                return ParseHotkey(value.GetString()) is { } h ? s with { HotkeyConvert = h } : s;
            case "apps.layout" when Strings(value) is { } apps:
                return s with { ExcludedApps = apps.Select(a => a.ToLowerInvariant()).Distinct().ToArray() };
            case "words.layout" when Strings(value) is { } words:
                return s with { LearnedWords = words };
            default:
                return s;
        }
    }

    /// Слово в «Не переключать», набранное руками.
    public static AppSettings AddWord(AppSettings s, string raw)
    {
        var w = Lexicon.Normalize(raw.Trim());
        if (w.Length == 0 || s.LearnedWords.Contains(w)) return s;
        return s with { LearnedWords = [.. s.LearnedWords, w] };
    }

    /// Имя exe из выбранного файла — так их хранит список исключений.
    public static AppSettings AddApps(AppSettings s, IEnumerable<string> paths)
    {
        var added = paths.Select(p => Path.GetFileName(p).ToLowerInvariant())
            .Where(e => e.Length > 0 && !s.ExcludedApps.Contains(e)).Distinct().ToArray();
        return added.Length == 0 ? s : s with { ExcludedApps = [.. s.ExcludedApps, .. added] };
    }

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
