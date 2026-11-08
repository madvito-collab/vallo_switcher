namespace Vallo.Core;

/// Смайлики из скобок: «ок))» + пробел → «ок😄». Наборы — «Миллениал», «Зумер» или свой. Порт `EmojiBrackets.swift`.
/// - срабатывает на пробеле, Enter или Tab после скобок — раньше не знаем, будет ли ещё скобка;
/// - `)` — не смайлик, если закрывает открытую в этой строке `(` («(вот так)») или стоит после цифры («1) пункт»);
/// - `(` — смайлик только с пробелом после: в тексте открывающая прилипает к слову («(вот»);
/// - `:)`, `;(` и подобные — уже смайлики, не трогаем;
/// - скобок больше, чем в наборе, — берём самый длинный из того же вида.
public sealed class EmojiBrackets
{
    public IReadOnlyDictionary<string, string> Pairs { get; }

    public EmojiBrackets(IReadOnlyDictionary<string, string> pairs)
    {
        Pairs = pairs.Where(p => p.Value.Length > 0 && p.Key.Length > 0 && (p.Key.All(c => c == ')') || p.Key.All(c => c == '(')))
            .ToDictionary(p => p.Key, p => p.Value);
    }

    public static readonly EmojiBrackets Millennial = new(new Dictionary<string, string>
    {
        [")"] = "🙂", ["))"] = "😄", [")))"] = "😂", ["("] = "🙁", ["(("] = "😢", ["((("] = "😭",
    });
    public static readonly EmojiBrackets Zoomer = new(new Dictionary<string, string>
    {
        [")"] = "😊", ["))"] = "😭", [")))"] = "💀", ["("] = "🥲", ["(("] = "😔", ["((("] = "🥺",
    });

    static readonly HashSet<string> Boundaries = [" ", "\n", "\r", "\t"];
    /// Перед скобкой — уже смайлик из знаков (`:)`, `;(`, `=)`, `^)`).
    static readonly HashSet<string> EmoticonEyes = [":", ";", "=", "^", "8", "-"];

    /// `raw` — текст (символами, как их видит человек), только что дополненный границей.
    /// Скобки перед границей стали смайликом — новый текст, иначе null. Первые `frozen` символов не трогаем.
    public List<string>? Apply(List<string> raw, int frozen = 0)
    {
        if (raw.Count == 0 || !Boundaries.Contains(raw[^1])) return null;
        var boundary = raw[^1];
        var body = raw.GetRange(0, raw.Count - 1);
        if (body.Count == 0 || (body[^1] != ")" && body[^1] != "(")) return null;
        var bracket = body[^1];
        var start = body.Count - 1;
        while (start > 0 && body[start - 1] == bracket) start--;
        if (start < frozen) return null;
        if (start > 0)
        {
            var b = body[start - 1];
            if (EmoticonEyes.Contains(b)) return null;
            if (bracket == ")" && b.Length > 0 && char.IsDigit(b[0])) return null;
        }
        if (bracket == ")")
        {
            // закрывает открытую в этой строке скобку — это текст
            var lineStart = body.FindLastIndex(start - 1 < 0 ? 0 : start - 1, e => e == "\n" || e == "\r") + 1;
            if (start == 0) lineStart = 0;
            var line = body.GetRange(lineStart, start - lineStart);
            if (line.Count(e => e == "(") > line.Count(e => e == ")")) return null;
        }
        var count = body.Count - start;
        var best = Pairs.Keys.Where(k => k[0] == bracket[0] && k.Length <= count).OrderByDescending(k => k.Length).FirstOrDefault();
        if (best is null) return null;
        var result = body.GetRange(0, start);
        result.AddRange(TextElements.Split(Pairs[best]));
        result.Add(boundary);
        return result;
    }
}
