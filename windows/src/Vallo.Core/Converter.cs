using System.Text;

namespace Vallo.Core;

/// Переводчик: перекладывает текст с одной раскладки на другую по физическим клавишам.
public static class Converter
{
    /// Что дадут эти нажатия в данной раскладке. Нажатия без символа пропускаются.
    /// Мёртвая клавиша печатает вместе со следующей («'» + «[» → «'[»); последняя мёртвая — ещё ничего.
    public static string Render(IEnumerable<KeyStroke> strokes, IKeyLayout layout)
    {
        var sb = new StringBuilder();
        KeyStroke? pending = null;
        foreach (var s in strokes)
        {
            if (pending is { } d)
            {
                sb.Append(layout.Compose(d, s));
                pending = null;
            }
            else if (layout.DeadKey(s) is not null) pending = s;
            else if (layout.Character(s) is { } c) sb.Append(c);
        }
        return sb.ToString();
    }

    /// Какими нажатиями набрать текст в данной раскладке. null — символа в раскладке нет.
    public static List<KeyStroke?> Strokes(string text, IKeyLayout layout)
    {
        var reverse = ReverseTable(layout);
        return text.Select(ch => reverse.TryGetValue(ch.ToString(), out var s) ? (KeyStroke?)s : null).ToList();
    }

    /// Текст, набранный в `from`, как он выглядел бы в `to`. Символы, которых нет в `from`, остаются как есть.
    public static string Convert(string text, IKeyLayout from, IKeyLayout to)
    {
        var reverse = ReverseTable(from);
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            var key = ch.ToString();
            if (reverse.TryGetValue(key, out var stroke) && to.Character(stroke) is { } o) sb.Append(o);
            else sb.Append(key);
        }
        return sb.ToString();
    }

    /// символ → нажатие. Если символ дают несколько клавиш — без Caps Lock и с меньшим кодом
    /// (основной блок, а не цифровая клавиатура). У Windows VK до 255.
    internal static Dictionary<string, KeyStroke> ReverseTable(IKeyLayout layout)
    {
        var result = new Dictionary<string, KeyStroke>();
        for (int code = 0; code <= 255; code++)
            foreach (var caps in new[] { false, true })
                foreach (var shift in new[] { false, true })
                {
                    var stroke = new KeyStroke((ushort)code, shift, caps);
                    // мёртвая клавиша тоже даёт свой знак («'» в «США — международной»): выделенный текст с ним переводим
                    if ((layout.Character(stroke) ?? layout.DeadKey(stroke)) is not { } ch) continue;
                    if (result.TryGetValue(ch, out var existing) && !IsBetter(stroke, existing)) continue;
                    result[ch] = stroke;
                }
        return result;
    }

    static bool IsBetter(KeyStroke a, KeyStroke b)
    {
        if (a.CapsLock != b.CapsLock) return !a.CapsLock;
        // на Windows цифровая клавиатура (VK 0x60–0x6F) идёт раньше основного блока по коду —
        // без этого «.» нашёлся бы на Num Decimal, а не на клавише «ю»
        if (IsNumpad(a) != IsNumpad(b)) return !IsNumpad(a);
        return a.KeyCode < b.KeyCode;
    }

    public static bool IsNumpad(KeyStroke s) => s.KeyCode >= 0x60 && s.KeyCode <= 0x6F;
}
