using System.Text;
using System.Text.RegularExpressions;

namespace Vallo.Core;

/// Телефоны и разряды чисел — часть типографики. Порт маковского `NumbersPhones.swift`.
/// Телефон: +7 или 8 и ровно 10 цифр после: мобильный (код на 9) → «+7 916 123-45-67», городской → «+7 (812) 555-55-55»,
/// «8 800» остаётся с восьмёркой. Разряды: от 5 цифр всегда, четыре — только рядом с валютой (не трогаем годы).
/// На лету (`final: false`) число должно быть закончено — после него уже стоит пробел или знак.
static class NumbersPhones
{
    const string Nbsp = Typographer.Nbsp;
    const string Sep = "[ \\u00A0\\-()]*";
    // буква, но не метка «слева неизвестно что» ([\p{L}-[ꙮ]]): номер в начале поля после клика — тоже номер
    static readonly Regex Phone = new(
        "(?<![\\d+#№@/._" + "\\p{L}-[" + Typographer.UnknownLeft + "]])(\\+7|8)" + Sep + "(\\d)" + Sep + "(\\d)" + Sep + "(\\d)"
        + string.Concat(Enumerable.Repeat(Sep + "(\\d)", 7)) + "(?![\\d\\p{L}@/_])", RegexOptions.CultureInvariant);

    public static string Phones(string s, bool final) => Replace(Phone, s, final, m =>
    {
        var prefix = m.Groups[1].Value;
        var d = string.Concat(Enumerable.Range(2, 10).Select(i => m.Groups[i].Value));
        var code = d[..3];
        var tail = $"{d[3..6]}-{d[6..8]}-{d[8..]}";
        if (code == "800") return $"{(prefix == "8" ? "8" : "+7")}{Nbsp}800{Nbsp}{tail}";
        if (code[0] == '9') return $"+7{Nbsp}{code}{Nbsp}{tail}";
        return $"+7{Nbsp}({code}){Nbsp}{tail}";
    }, wholeMatch: true);

    /// Слева от числа нельзя: цифра, буква, знаки адресов и кодов (№, #, @, /, _, :), точка или запятая, плюс;
    /// «№ 12345» — номер, не сумма.
    static readonly string Before = "(?<![\\d#№@/_:.,+" + "\\p{L}-[" + Typographer.UnknownLeft + "]])(?<!№[ \\u00A0])";
    static readonly Regex Big = new(Before + "([1-9]\\d{4,9})(?![\\d\\p{L}@/_:])", RegexOptions.CultureInvariant);
    static readonly Regex MoneyAfter = new(Before + "([1-9]\\d{3})(?=[ \\u00A0]?[₽$€])", RegexOptions.CultureInvariant);
    static readonly Regex MoneyBefore = new("(?<=[$€₽])[ \\u00A0]?([1-9]\\d{3})(?![\\d\\p{L}])", RegexOptions.CultureInvariant);

    public static string Groups(string s, bool final)
    {
        var o = Replace(Big, s, final, m => Grouped(m.Groups[1].Value), wholeMatch: false);
        o = Replace(MoneyAfter, o, true, m => Grouped(m.Groups[1].Value), wholeMatch: false);
        o = Replace(MoneyBefore, o, final, m => Grouped(m.Groups[1].Value), wholeMatch: true);
        return o;
    }

    /// «1234567» → «1 234 567» (неразрывные пробелы).
    public static string Grouped(string digits)
    {
        var parts = new List<string>();
        var rest = digits;
        while (rest.Length > 3)
        {
            parts.Insert(0, rest[^3..]);
            rest = rest[..^3];
        }
        parts.Insert(0, rest);
        return string.Join(Nbsp, parts);
    }

    /// Замены с конца, чтобы не съезжали позиции. На лету совпадение у самого конца текста ждёт следующего символа.
    /// `wholeMatch` — заменить всё совпадение, иначе только группу 1.
    static string Replace(Regex regex, string s, bool final, Func<Match, string> make, bool wholeMatch)
    {
        var sb = new StringBuilder(s);
        foreach (var m in regex.Matches(s).Reverse())
        {
            if (!final && m.Index + m.Length >= s.Length) continue;
            var (at, len) = wholeMatch ? (m.Index, m.Length) : (m.Groups[1].Index, m.Groups[1].Length);
            sb.Remove(at, len).Insert(at, make(m));
        }
        return sb.ToString();
    }
}
