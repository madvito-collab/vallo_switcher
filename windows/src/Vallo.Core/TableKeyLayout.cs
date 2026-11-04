using System.Text.Json;

namespace Vallo.Core;

/// Раскладка: что даёт каждое нажатие.
public interface IKeyLayout
{
    Language Language { get; }
    string? Character(KeyStroke s);
}

/// Раскладка-таблица, снятая с системы.
/// JSON: {"language":"ru","entries":[{"keyCode":5,"shift":false,"capsLock":false,"char":"п"}, ...]}
public sealed class TableKeyLayout : IKeyLayout
{
    public Language Language { get; }
    public IReadOnlyDictionary<KeyStroke, string> Table { get; }

    public TableKeyLayout(Language language, IReadOnlyDictionary<KeyStroke, string> table)
    {
        Language = language;
        Table = table;
    }

    public string? Character(KeyStroke s) => Table.TryGetValue(s, out var c) ? c : null;

    public static TableKeyLayout Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (!LanguageExtensions.TryParse(root.GetProperty("language").GetString(), out var language))
            throw new InvalidDataException($"{path}: неизвестный language");
        var table = new Dictionary<KeyStroke, string>();
        foreach (var e in root.GetProperty("entries").EnumerateArray())
        {
            var stroke = new KeyStroke(e.GetProperty("keyCode").GetUInt16(),
                e.GetProperty("shift").GetBoolean(), e.GetProperty("capsLock").GetBoolean());
            table[stroke] = e.GetProperty("char").GetString()!;
        }
        return new TableKeyLayout(language, table);
    }
}
