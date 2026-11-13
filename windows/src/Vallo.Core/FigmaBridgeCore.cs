using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vallo.Core;

// Связь свитчера с плагином Фигмы: сообщения, точечные правки текста, статистика исправлений.
// Протокол — figma-plugin/PROTOCOL.md, общий с Маком. Перенос `Sources/ValloCore/FigmaBridgeCore.swift`.

public sealed record FigmaText(string Id, string Text);

/// Замена куска исходного текста: [Start, End) в единицах UTF-16 (как индексы строк в JS и C#).
public readonly record struct TextEdit(int Start, int End, string Insert);

public sealed record FigmaTextEdits(string Id, IReadOnlyList<TextEdit> Edits);

/// Что исправлено — для окошка плагина.
public sealed record FixStats
{
    public int Commas { get; set; }
    public int Typos { get; set; }
    public int Yo { get; set; }
    public int Nbsp { get; set; }
    public int Dashes { get; set; }
    public int Quotes { get; set; }
    public int Other { get; set; }
    /// Сколько слоёв изменилось.
    public int Layers { get; set; }

    public bool IsEmpty => Commas + Typos + Yo + Nbsp + Dashes + Quotes + Other == 0;

    public void Add(FixStats o)
    {
        Commas += o.Commas; Typos += o.Typos; Yo += o.Yo; Nbsp += o.Nbsp;
        Dashes += o.Dashes; Quotes += o.Quotes; Other += o.Other; Layers += o.Layers;
    }
}

public abstract record FigmaMode
{
    /// Человек редактирует текст — работать по-старому, с выделенным текстом.
    public sealed record Text : FigmaMode;
    /// Ничего не выделено.
    public sealed record Empty : FigmaMode;
    public sealed record Layers(IReadOnlyList<FigmaText> Texts) : FigmaMode;
}

public abstract record FigmaMessage
{
    // плагин → свитчер
    /// `Fresh` — плагин только что запущен (а не переподключился): ему сразу нужна работа.
    public sealed record Hello(int Version, string File, bool Fresh) : FigmaMessage;
    /// `Skipped` — слои, которые нельзя менять (нет шрифта).
    public sealed record Mode(int Id, FigmaMode Kind, int Skipped) : FigmaMessage;
    public sealed record Done(int Id) : FigmaMessage;
    // свитчер → плагин
    public sealed record Press(int Id) : FigmaMessage;
    public sealed record Result(int Id, IReadOnlyList<FigmaTextEdits> Edits, FixStats Stats, int Skipped) : FigmaMessage;

    /// Непонятное сообщение — null.
    public static FigmaMessage? Decode(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return null;
            int? Int(string key) => o[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
            string? Str(string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            var skipped = Int("skipped") ?? 0;
            switch (Str("type"))
            {
                case "hello":
                    if (Int("version") is not { } version || Str("file") is not { } file) return null;
                    var fresh = o["fresh"] is JsonValue f && f.TryGetValue<bool>(out var b) && b;
                    return new Hello(version, file, fresh);
                case "mode":
                    if (Int("id") is not { } mid) return null;
                    FigmaMode? kind = Str("mode") switch
                    {
                        "text" => new FigmaMode.Text(),
                        "empty" => new FigmaMode.Empty(),
                        "layers" => new FigmaMode.Layers(Texts(o["texts"])),
                        _ => null,
                    };
                    return kind is null ? null : new Mode(mid, kind, skipped);
                case "done":
                    return Int("id") is { } did ? new Done(did) : null;
                case "press":
                    return Int("id") is { } pid ? new Press(pid) : null;
                case "result":
                    if (Int("id") is not { } rid) return null;
                    var stats = o["stats"]?.Deserialize<FixStats>(WireOptions) ?? new FixStats();
                    var edits = o["edits"]?.Deserialize<List<FigmaTextEdits>>(WireOptions) ?? [];
                    return new Result(rid, edits, stats, skipped);
                default:
                    return null;
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    static List<FigmaText> Texts(JsonNode? node)
    {
        if (node is not JsonArray array) return [];
        var list = new List<FigmaText>();
        foreach (var item in array)
        {
            // слой без id или текста — сообщение битое
            var id = item?["id"]?.GetValue<string>() ?? throw new FormatException();
            var text = item?["text"]?.GetValue<string>() ?? throw new FormatException();
            list.Add(new FigmaText(id, text));
        }
        return list;
    }

    /// JSON для плагина: ключи по алфавиту, русские буквы как есть.
    public string Encode()
    {
        var o = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
        switch (this)
        {
            case Hello h:
                o["type"] = "hello"; o["version"] = h.Version; o["file"] = h.File; o["fresh"] = h.Fresh;
                break;
            case Mode m:
                o["type"] = "mode"; o["id"] = m.Id; o["skipped"] = m.Skipped;
                switch (m.Kind)
                {
                    case FigmaMode.Text: o["mode"] = "text"; break;
                    case FigmaMode.Empty: o["mode"] = "empty"; break;
                    case FigmaMode.Layers l: o["mode"] = "layers"; o["texts"] = JsonSerializer.SerializeToNode(l.Texts, WireOptions); break;
                }
                break;
            case Done d:
                o["type"] = "done"; o["id"] = d.Id;
                break;
            case Press p:
                o["type"] = "press"; o["id"] = p.Id;
                break;
            case Result r:
                o["type"] = "result"; o["id"] = r.Id; o["skipped"] = r.Skipped;
                o["edits"] = JsonSerializer.SerializeToNode(r.Edits, WireOptions);
                o["stats"] = JsonSerializer.SerializeToNode(r.Stats, WireOptions);
                break;
        }
        var json = new JsonObject();
        foreach (var (k, v) in o) json[k] = v;
        return json.ToJsonString(WireOptions);
    }

    static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        IgnoreReadOnlyProperties = true,
    };
}

// MARK: - Правки

public static class TextDiff
{
    /// Больше этого (произведение длин после обрезки общего начала и конца) — одна правка на всю середину,
    /// чтобы не зависнуть на огромном слое.
    const long MaxCells = 4_000_000;

    /// Точечные правки, превращающие `old` в `new`: по возрастанию, не пересекаются, смещения — в исходном тексте.
    /// Сравнение — по символам Юникода (не режет эмодзи пополам), смещения — в UTF-16.
    public static List<TextEdit> Edits(string old, string @new) =>
        Edits(old.EnumerateRunes().ToArray(), @new.EnumerateRunes().ToArray());

    static string Join(ReadOnlySpan<Rune> runes)
    {
        var sb = new StringBuilder();
        foreach (var r in runes) sb.Append(r.ToString());
        return sb.ToString();
    }

    static List<TextEdit> Edits(Rune[] a, Rune[] b)
    {
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix]) suffix++;
        var midA = a[prefix..(a.Length - suffix)];
        var midB = b[prefix..(b.Length - suffix)];
        if (midA.Length == 0 && midB.Length == 0) return [];

        // смещение в UTF-16 для каждого индекса символа старой строки
        var offset = new int[a.Length + 1];
        for (var i = 0; i < a.Length; i++) offset[i + 1] = offset[i] + a[i].Utf16SequenceLength;

        int n = midA.Length, m = midB.Length;
        var newline = new Rune('\n');
        // длинный слой: таблица не влезает — сравниваем построчно (переносы строк правила не трогают),
        // чтобы правки остались точечными и не слетело оформление
        if ((long)n * m > MaxCells)
        {
            var linesA = SplitLines(midA, newline);
            var linesB = SplitLines(midB, newline);
            if (linesA.Count == linesB.Count && linesA.Count > 1)
            {
                var result0 = new List<TextEdit>();
                var @base = offset[prefix];
                for (var k = 0; k < linesA.Count; k++)
                {
                    foreach (var e in Edits(linesA[k], linesB[k]))
                        result0.Add(new TextEdit(@base + e.Start, @base + e.End, e.Insert));
                    @base += linesA[k].Sum(r => r.Utf16SequenceLength) + 1;
                }
                return result0;
            }
        }

        // пары (i, j) совпавших символов внутри середин
        var matches = new List<(int, int)>();
        if (n > 0 && m > 0 && (long)n * m <= MaxCells)
        {
            // LCS: таблица длин с конца
            var w = m + 1;
            var t = new ushort[(n + 1) * w];
            for (var i = n - 1; i >= 0; i--)
                for (var j = m - 1; j >= 0; j--)
                    t[i * w + j] = midA[i] == midB[j] ? (ushort)(t[(i + 1) * w + j + 1] + 1)
                                                      : Math.Max(t[(i + 1) * w + j], t[i * w + j + 1]);
            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (midA[x] == midB[y]) { matches.Add((x, y)); x++; y++; }
                else if (t[(x + 1) * w + y] >= t[x * w + y + 1]) x++;
                else y++;
            }
        }

        // промежутки между совпадениями — правки
        var result = new List<TextEdit>();
        int pi = 0, pj = 0;
        matches.Add((n, m));
        foreach (var (mi, mj) in matches)
        {
            if (mi > pi || mj > pj)
                result.Add(new TextEdit(offset[prefix + pi], offset[prefix + mi], Join(midB.AsSpan(pj, mj - pj))));
            pi = mi + 1;
            pj = mj + 1;
        }
        return result;
    }

    static List<Rune[]> SplitLines(Rune[] runes, Rune separator)
    {
        var lines = new List<Rune[]>();
        var start = 0;
        for (var i = 0; i <= runes.Length; i++)
        {
            if (i < runes.Length && runes[i] != separator) continue;
            lines.Add(runes[start..i]);
            start = i + 1;
        }
        return lines;
    }
}

// MARK: - Счётчики

/// Сколько исправлено на шаге: сравнение текста до и после шага.
public static class FixCounter
{
    static int Increase(string a, string b, string set) =>
        Math.Max(0, b.Count(set.Contains) - a.Count(set.Contains));

    public static int Commas(string a, string b) => Increase(a, b, ",");

    public static int Yo(string a, string b) => Increase(a, b, "ёЁ");

    /// Сколько слов исправлено: слова старого текста, которых нет в общей части со новым.
    public static int Typos(string a, string b)
    {
        var wa = a.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var wb = b.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var prev = new int[wb.Length + 1];
        var cur = new int[wb.Length + 1];
        for (var i = 1; i <= wa.Length; i++)
        {
            for (var j = 1; j <= wb.Length; j++)
                cur[j] = wa[i - 1] == wb[j - 1] ? prev[j - 1] + 1 : Math.Max(prev[j], cur[j - 1]);
            (prev, cur) = (cur, prev);
        }
        return wa.Length - prev[wb.Length];
    }

    public static (int Nbsp, int Dashes, int Quotes, int Other) Typography(string a, string b) =>
        (Increase(a, b, "\u00A0"),
         Increase(a, b, "—–−"),
         Increase(a, b, "«»„“”‘’"),
         Increase(a, b, "…×©®™±→←½₽"));
}

// MARK: - Конвейер для слоя

/// Последний шаг обработки слоя: типографика + статистика по всем шагам.
/// Опечатки, «ё» и запятые делаются снаружи — сюда приходят их результаты.
public static class FixPipeline
{
    public static (string Text, FixStats Stats) Finish(string original, string typoFixed, string yoFixed, string? withCommas)
    {
        var commaFixed = withCommas ?? yoFixed;
        var result = Typographer.Format(commaFixed);
        var t = FixCounter.Typography(commaFixed, result);
        var stats = new FixStats
        {
            Typos = FixCounter.Typos(original, typoFixed),
            Yo = FixCounter.Yo(typoFixed, yoFixed),
            Commas = FixCounter.Commas(yoFixed, commaFixed),
            Nbsp = t.Nbsp, Dashes = t.Dashes, Quotes = t.Quotes, Other = t.Other,
            Layers = result != original ? 1 : 0,
        };
        return (result, stats);
    }
}
