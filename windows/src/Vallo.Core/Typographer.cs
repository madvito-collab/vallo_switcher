using System.Text;
using System.Text.RegularExpressions;

namespace Vallo.Core;

/// Типографика по «Ководству» Лебедева: тире, минусы, кавычки, неразрывные пробелы, символы, телефоны, разряды.
/// Порт маковского `Typographer.swift`; общие примеры — `Data/tests/typography-cases.tsv`.
/// Один метод на оба режима: выделенный текст целиком (`final: true`) и набор на лету (`final: false`),
/// где правила, которым нужен следующий символ, ждут его.
public static class Typographer
{
    public const string Nbsp = " ";
    /// Метка «слева неизвестно что» (после клика или сброса посреди текста): это буква,
    /// поэтому правила начала строки и слова рядом с ней не срабатывают.
    public const char UnknownLeft = 'ꙮ';

    /// Предлоги, союзы и частицы, после которых ставится неразрывный пробел.
    static readonly string[] ShortWords = ["а", "в", "во", "и", "к", "ко", "о", "об", "от", "с", "со", "у", "на", "по", "за", "до",
                                           "из", "но", "не", "ни", "для", "без", "под", "над", "при", "про", "через"];
    /// Частицы, перед которыми ставится неразрывный пробел.
    static readonly string[] Particles = ["ли", "ль", "же", "ж", "бы", "б"];

    /// `hint` — язык для кавычек, если рядом нет букв (на лету: текущая раскладка).
    public static string Format(string text, bool final = true, Language hint = Language.Ru)
    {
        // телефоны — до правил: иначе «123-45» в номере стало бы диапазоном «123–45»
        var s = NumbersPhones.Phones(text, final);
        foreach (var (regex, template) in Rules(final)) s = regex.Replace(s, template);
        // разряды — после: «5000р» к этому времени уже «5000 ₽»
        s = NumbersPhones.Groups(s, final);
        return Quotes(s, hint);
    }

    // ---- Правила-замены

    static readonly Dictionary<bool, (Regex, string)[]> Cache = new();
    static readonly object Gate = new();

    static (Regex, string)[] Rules(bool final)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(final, out var cached)) return cached;
            var end = final ? "|\\z" : "";
            const string sp = "[ \\u00A0]";
            var words = string.Join("|", ShortWords);
            var parts = string.Join("|", Particles);
            (string, string)[] raw =
            [
                // чистка пробелов: двойной пробел — только в законченном тексте; пробел перед знаком — если за знаком
                // пробел или конец, иначе это смайлик «:)» или «.env»
                final ? ("(?<=\\S) {2,}", " ") : ("(?!)", ""),
                (" +(?=[,.!?:;](?:\\s" + end + "))", ""),
                // символы
                ("(?i)\\([cс]\\)", "©"),
                ("(?i)\\(r\\)", "®"),
                ("(?i)\\(tm\\)", "™"),
                ("\\+-", "±"),
                ("->", "→"),
                ("<-", "←"),
                // то же, набранное в русской раскладке: Shift+Ю — это «>», Shift+Б — «<»
                ("-Ю", "→"),
                ("Б-", "←"),
                ("(?<![\\d/])1/2(?=[^\\d/]" + end + ")", "½"),
                // умножение: 5x3, 5*3, 5 х 3; не 0x1F
                ("(?<=\\d)(?<!\\b0)[xх*](?=\\d)", "×"),
                ("(\\d)(?:[ \\u00A0][xх*][ \\u00A0]?|[xх*][ \\u00A0])(?=\\d)", "$1 × "),
                ("(?<!\\.)\\.{3}(?!\\.)", "…"),
                // рубль: «850р», «850 руб» → «850 ₽»; «руб.» с точкой не трогаем
                ("(?i)(\\d)[ \\u00A0]?(?:руб|р)(?=[\\s,;:!?)»”]" + end + ")", "$1" + Nbsp + "₽"),
                // цифры: диапазон (не даты, не телефоны, не ссылки и имена файлов), минус
                ("(?<![\\d+\\-–/.])(\\d{1,4})-(\\d{1,4})(?=[\\s,;:!?)»”]|\\.\\s" + (final ? "|\\.\\z|\\z" : "") + ")", "$1–$2"),
                ("(\\d) - (?=\\d)", "$1 − "),
                ("(^|[ \\u00A0(])-(?=\\d)", "$1−"),
                // тире: в начале строки (диалог) — только в законченном тексте; между словами; после цифры — если справа не цифра
                final ? ("(^|\\n)-{1,2} ", "$1— ") : ("(?!)", ""),
                ("([^\\s\\d])" + sp + "+-{1,2}" + sp + "+", "$1" + Nbsp + "— "),
                ("(\\d)" + sp + "+-{1,2}" + sp + "+(?=[^\\d\\s]" + end + ")", "$1" + Nbsp + "— "),
                ("([^\\s]) —", "$1" + Nbsp + "—"),
                // неразрывные пробелы
                ("(?i)(?<![\\p{L}\\p{N}'’\\-])(" + words + ") ", "$1" + Nbsp),
                ("(?i)(?<=\\p{L}) (" + parts + ")(?=[\\s.,!?;:…)»“”]" + end + ")", Nbsp + "$1"),
                ("(?<=\\d) (?=\\p{L})", Nbsp),
                ("(?<=\\d) (?=\\d{3}(?:[^\\d]" + end + "))", Nbsp),
                ("(?<![\\p{L}])(\\p{Lu})\\. (?=\\p{Lu})", "$1." + Nbsp),
                ("([№§]) ", "$1" + Nbsp),
                // знак валюты не отрывается от суммы
                ("(?<=\\d) (?=[₽$€])", Nbsp),
            ];
            var compiled = raw.Select(r => (new Regex(r.Item1, RegexOptions.Multiline | RegexOptions.CultureInvariant), r.Item2)).ToArray();
            Cache[final] = compiled;
            return compiled;
        }
    }

    // ---- Кавычки

    static readonly Dictionary<char, char> Closer = new() { ['«'] = '»', ['„'] = '“', ['“'] = '”', ['‘'] = '’' };

    /// Прямые кавычки → «ёлочки» с „лапками“ внутри (русский) или “лапки” с ‘одинарными’ (английский).
    /// Апостроф между буквами → ’. Уже поставленные кавычки учитываются во вложенности.
    public static string Quotes(string text, Language hint)
    {
        if (text.IndexOf('"') < 0 && text.IndexOf('\'') < 0) return text;
        var chars = text.ToCharArray();
        var stack = new List<char>();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            char? prev = i > 0 ? chars[i - 1] : null;
            switch (c)
            {
                case '\'':
                    if (prev is { } p && char.IsLetter(p) && i + 1 < chars.Length && char.IsLetter(chars[i + 1])) chars[i] = '’';
                    break;
                case '«': case '„':
                    stack.Add(c);
                    break;
                case '»':
                    if (stack.Count > 0 && stack[^1] == '«') stack.RemoveAt(stack.Count - 1);
                    break;
                case '“':
                    // в русском “ закрывает „, в английском — открывает
                    if (stack.Count > 0 && stack[^1] == '„') stack.RemoveAt(stack.Count - 1); else stack.Add('“');
                    break;
                case '”':
                    if (stack.Count > 0 && stack[^1] == '“') stack.RemoveAt(stack.Count - 1);
                    break;
                case '‘':
                    stack.Add('‘');
                    break;
                case '’':
                    if (stack.Count > 0 && stack[^1] == '‘') stack.RemoveAt(stack.Count - 1);
                    break;
                case '"':
                    if (prev is { } pr && stack.Count > 0 && pr == stack[^1])
                    {
                        // кавычка сразу за открывающей — пустая пара «», а не вложенная «„
                        var opener = stack[^1];
                        stack.RemoveAt(stack.Count - 1);
                        chars[i] = Closer.GetValueOrDefault(opener, '»');
                    }
                    else if (IsOpeningPosition(prev))
                    {
                        var open = LanguageAround(chars, i, hint) == Language.Ru
                            ? (stack.Count == 0 ? '«' : '„')
                            : (stack.Count == 0 ? '“' : '‘');
                        chars[i] = open;
                        stack.Add(open);
                    }
                    else if (stack.Count > 0)
                    {
                        var opener = stack[^1];
                        stack.RemoveAt(stack.Count - 1);
                        chars[i] = Closer.GetValueOrDefault(opener, '»');
                    }
                    else
                    {
                        // начало цитаты не видно — после буквы кавычка всё равно закрывающая
                        chars[i] = LanguageAround(chars, i, hint) == Language.Ru ? '»' : '”';
                    }
                    break;
            }
        }
        return new string(chars);
    }

    static bool IsOpeningPosition(char? prev)
    {
        if (prev is not { } p || p == UnknownLeft) return true;
        return char.IsWhiteSpace(p) || "([{—–-«„".Contains(p);
    }

    /// Язык по ближайшей букве слева, а если слева букв нет — справа.
    static Language LanguageAround(char[] chars, int i, Language hint)
    {
        char? letter = null;
        for (var j = i - 1; j >= 0 && letter is null; j--)
            if (char.IsLetter(chars[j]) && chars[j] != UnknownLeft) letter = chars[j];
        for (var j = i + 1; j < chars.Length && letter is null; j++)
            if (char.IsLetter(chars[j]) && chars[j] != UnknownLeft) letter = chars[j];
        if (letter is not { } l) return hint;
        return Lexicon.IsInAlphabet(char.ToLowerInvariant(l), Language.Ru) ? Language.Ru : Language.En;
    }
}
