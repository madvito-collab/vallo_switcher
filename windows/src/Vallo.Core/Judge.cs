namespace Vallo.Core;

public abstract record Verdict
{
    public static readonly Verdict Wait = new WaitVerdict();
    public static readonly Verdict Keep = new KeepVerdict();

    sealed record WaitVerdict : Verdict;
    sealed record KeepVerdict : Verdict;

    public sealed record SwitchLanguage(string Reason) : Verdict;
}

/// Судья: решает, набрано ли слово не в той раскладке.
/// `current` — как слово выглядит сейчас, `alternative` — те же клавиши в другой раскладке.
public sealed class Judge(Lexicon en, Lexicon ru, ISet<string> learned)
{
    /// На сколько триграммы другого языка должны быть «похожее», чтобы переключить незнакомое слово.
    public const double TrigramMargin = 2.0;
    /// Раньше этой буквы ничего не решаем.
    public const int MinLetters = 3;

    static SwitcherRules Rules => SwitcherRules.Shared;

    public ISet<string> Learned { get; set; } = learned;

    Lexicon Lex(Language language) => language == Language.En ? en : ru;

    static string Up(Language l) => l.Code().ToUpperInvariant();

    /// Сокращения единиц и денег сразу после числа («850руб», «5 шт») — не ошибка раскладки.
    public static bool IsUnitAfterNumber(string word, Language language) =>
        Rules.UnitsAfterNumber[language].Contains(Core(word).ToLowerInvariant());

    /// Во время набора: переключаем, только если в текущем языке такое начало невозможно,
    /// а в другом нормальное.
    public Verdict MidWord(string current, string alternative, Language currentLanguage)
    {
        string cur = Core(current), alt = Core(alternative);
        if (SpecialText.IsSpecial(current) || SpecialText.IsSpecial(alternative)) return Verdict.Wait;
        if (IsAbbreviation(current)) return Verdict.Wait;
        if (cur.Length < MinLetters) return Verdict.Wait;
        var normalized = Lexicon.Normalize(cur);
        if (Learned.Any(w => w.StartsWith(normalized, StringComparison.Ordinal))) return Verdict.Wait;
        if (IsSlang(cur, currentLanguage)) return Verdict.Wait;
        Lexicon L = Lex(currentLanguage), A = Lex(currentLanguage.Other());
        if (!L.IsPrefix(cur) && A.IsPrefix(alt))
            return new Verdict.SwitchLanguage($"«{cur}» не начало {Up(currentLanguage)}-слова, «{alt}» — начало {Up(currentLanguage.Other())}");
        return Verdict.Wait;
    }

    /// На конце слова: страховка, если по началу решить не удалось.
    public Verdict WordEnd(string current, string alternative, Language currentLanguage)
    {
        string cur = Core(current), alt = Core(alternative);
        var other = currentLanguage.Other();
        if (SpecialText.IsSpecial(current) || SpecialText.IsSpecial(alternative)) return Verdict.Keep;
        // заглавными — не переводим, кроме известной аббревиатуры другого языка («ЗВА» → «PDF»)
        if (IsAbbreviation(current) && !IsKnownAbbreviation(alternative, other)) return Verdict.Keep;
        if (Learned.Contains(Lexicon.Normalize(cur))) return Verdict.Keep;
        if (IsSlang(cur, currentLanguage)) return Verdict.Keep;
        if (IsShort(cur, alt))
        {
            var shortWords = Rules.ShortWords;
            if (!(shortWords[other].Contains(alt.ToLowerInvariant()) || IsKnownAbbreviation(alt, other))
                || shortWords[currentLanguage].Contains(cur.ToLowerInvariant()))
                return Verdict.Keep;
            return new Verdict.SwitchLanguage($"«{alt}» — частое короткое слово {Up(other)}");
        }
        Lexicon L = Lex(currentLanguage), A = Lex(other);
        // букв мало только здесь («,sk» — «был», «;r[» — «жкх»): решает лишь то, слово ли другой вариант
        if (cur.Length < MinLetters)
        {
            if (!(A.IsWord(alt) || IsKnownAbbreviation(alt, other))) return Verdict.Keep;
            return new Verdict.SwitchLanguage($"«{alt}» — слово {Up(other)}");
        }
        if (L.IsWord(cur)) return Verdict.Keep;
        if (A.IsWord(alt)) return new Verdict.SwitchLanguage($"«{alt}» — слово {Up(other)}, «{cur}» — нет");
        var margin = A.TrigramScore(alt) - L.TrigramScore(cur);
        if (margin > TrigramMargin)
            return new Verdict.SwitchLanguage($"«{alt}» похоже на {Up(other)} сильнее на {margin.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        return Verdict.Keep;
    }

    /// После автопереключения: вернуть назад, если исправленный вариант перестал быть началом слова,
    /// а исходный выглядит правдоподобнее.
    public bool ShouldRevert(string original, string converted, Language originalLanguage)
    {
        Lexicon L = Lex(originalLanguage), A = Lex(originalLanguage.Other());
        string orig = Core(original), conv = Core(converted);
        return !A.IsPrefix(conv) && L.TrigramScore(orig) > A.TrigramScore(conv);
    }

    /// Короткое слово (1–2 буквы) в обеих раскладках. Буквы считаем в той, где их больше: «б» и «ю» в английской
    /// раскладке — запятая и точка, и «был» (,sk) иначе выглядел бы словом из двух букв.
    public static bool IsShort(string current, string alternative) =>
        Math.Max(Core(current).Length, Core(alternative).Length) < MinLetters;

    /// Частое короткое слово или словечко этого языка — кандидат, если контекст укажет на этот язык.
    public static bool IsShortCandidate(string word, Language language)
    {
        var w = Core(word).ToLowerInvariant();
        return w.Length > 0 && w.Length < MinLetters
            && (Rules.ShortWords[language].Contains(w) || Rules.Slang[language].Contains(w));
    }

    /// Слово целиком заглавными (от двух букв) — аббревиатура (ВГСЧ, ЖКХ, PDF): её не переводим,
    /// иначе тем, кто их часто пишет, придётся вносить каждую в исключения (решение автора).
    public static bool IsAbbreviation(string word)
    {
        var letters = word.Where(char.IsLetter).ToList();
        return letters.Count >= 2 && letters.All(char.IsUpper);
    }

    public static bool IsKnownAbbreviation(string word, Language language) =>
        Rules.Abbreviations[language].Contains(Core(word).ToLowerInvariant());

    static bool IsSlang(string word, Language language) =>
        Rules.Slang[language].Contains(word.ToLowerInvariant());

    /// Обрезает небуквенные символы в начале и в конце.
    public static string Core(string s)
    {
        var first = -1;
        for (var i = 0; i < s.Length; i++) if (char.IsLetter(s[i])) { first = i; break; }
        if (first < 0) return "";
        var last = s.Length - 1;
        while (!char.IsLetter(s[last])) last--;
        return s[first..(last + 1)];
    }
}

/// Ссылки, почта, телефоны, наборы с цифрами — автопереключение не трогает.
public static class SpecialText
{
    /// Домены верхнего уровня: слово, оканчивающееся на «.домен», — ссылка.
    public static IReadOnlyList<string> Domains => SwitcherRules.Shared.Domains;

    public static bool IsSpecial(string raw)
    {
        var s = raw.ToLowerInvariant();
        if (s.Any(char.IsNumber)) return true;
        if (s.Contains('@') || s.Contains("://", StringComparison.Ordinal) || s.Contains("www.", StringComparison.Ordinal)) return true;
        return Domains.Any(d => s.EndsWith("." + d, StringComparison.Ordinal));
    }
}
