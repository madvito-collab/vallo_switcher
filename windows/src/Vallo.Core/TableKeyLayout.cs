using System.Text.Json;

namespace Vallo.Core;

/// Раскладка: что даёт каждое нажатие.
public interface IKeyLayout
{
    Language Language { get; }
    string? Character(KeyStroke s);
    /// Мёртвая клавиша («США — международная»: апостроф, кавычка, ^, ~, `): сама ничего не печатает,
    /// ждёт следующую. Её знак сам по себе — для перевода выделенного текста.
    string? DeadKey(KeyStroke s) => null;
    /// Что напечатает мёртвая клавиша `dead`, если следом нажать `next` («'» + «[» → «'[», «'» + «c» → «ç»).
    string Compose(KeyStroke dead, KeyStroke next) => (DeadKey(dead) ?? "") + Character(next);
}

/// Мёртвая клавиша: её знак (с пробелом после неё) и что даёт с каждой следующей клавишей.
public sealed record DeadKeyInfo(string Spacing, IReadOnlyDictionary<KeyStroke, string> Compose);

/// Раскладка-таблица, снятая с системы.
/// JSON: {"language":"ru","entries":[{"keyCode":5,"shift":false,"capsLock":false,"char":"п"}, ...]}
public sealed class TableKeyLayout : IKeyLayout
{
    public Language Language { get; }
    public IReadOnlyDictionary<KeyStroke, string> Table { get; }

    /// Мёртвые клавиши: свой знак и что получается с каждой следующей клавишей (так, как ответила система).
    public IReadOnlyDictionary<KeyStroke, DeadKeyInfo> Dead { get; }

    public TableKeyLayout(Language language, IReadOnlyDictionary<KeyStroke, string> table,
                          IReadOnlyDictionary<KeyStroke, DeadKeyInfo>? dead = null)
    {
        Language = language;
        Table = table;
        Dead = dead ?? new Dictionary<KeyStroke, DeadKeyInfo>();
    }

    public string? Character(KeyStroke s) => Table.TryGetValue(s, out var c) ? c : null;

    public string? DeadKey(KeyStroke s) => Dead.TryGetValue(s, out var d) ? d.Spacing : null;

    public string Compose(KeyStroke dead, KeyStroke next) =>
        Dead.TryGetValue(dead, out var d) && d.Compose.TryGetValue(next, out var c) ? c : (DeadKey(dead) ?? "") + Character(next);

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
