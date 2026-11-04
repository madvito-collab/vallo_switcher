using System.Text.Json;

namespace Vallo.Core;

/// Списки правил свитчера (короткие слова, сленг, аббревиатуры, единицы, домены, частые ошибки).
/// Живут в `Data/switcher-rules.json` — общий файл для Мака и Windows; здесь только загрузка.
public sealed record SwitcherRules(
    IReadOnlyDictionary<Language, HashSet<string>> ShortWords,
    IReadOnlyDictionary<Language, HashSet<string>> Slang,
    IReadOnlyDictionary<Language, HashSet<string>> Abbreviations,
    IReadOnlyDictionary<Language, HashSet<string>> UnitsAfterNumber,
    IReadOnlyList<string> UnitsBeforeUnit,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> ExtraRussianWords,
    IReadOnlyDictionary<string, string> Misspellings)
{
    static readonly Lazy<SwitcherRules> shared = new(() => Load(FindFile()));

    public static SwitcherRules Shared => shared.Value;

    /// Рядом с exe (`Data/switcher-rules.json`), иначе вверх по папкам до `Data/` репозитория.
    static string FindFile()
    {
        var rel = Path.Combine("Data", "switcher-rules.json");
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, rel);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Нет switcher-rules.json (искали Data/switcher-rules.json от {AppContext.BaseDirectory} и выше)");
    }

    public static SwitcherRules Load(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            return new SwitcherRules(
                ByLanguage(r, "shortWords"), ByLanguage(r, "slang"), ByLanguage(r, "abbreviations"),
                ByLanguage(r, "unitsAfterNumber"),
                Strings(r.GetProperty("unitsBeforeUnit")), Strings(r.GetProperty("domains")),
                Strings(r.GetProperty("extraRussianWords")),
                r.GetProperty("misspellings").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException($"switcher-rules.json не читается ({path}): {e.Message}", e);
        }
    }

    static List<string> Strings(JsonElement a) => a.EnumerateArray().Select(x => x.GetString()!).ToList();

    /// JSON хранит языки строками «ru» / «en»; в коде — `Language`. Отсутствующий язык — пустой набор.
    static Dictionary<Language, HashSet<string>> ByLanguage(JsonElement root, string name)
    {
        var result = new Dictionary<Language, HashSet<string>>
        {
            [Language.En] = new(), [Language.Ru] = new(),
        };
        foreach (var p in root.GetProperty(name).EnumerateObject())
            if (LanguageExtensions.TryParse(p.Name, out var lang)) result[lang] = Strings(p.Value).ToHashSet();
        return result;
    }
}
