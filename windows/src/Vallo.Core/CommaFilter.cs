using System.Text;
using System.Text.RegularExpressions;

namespace Vallo.Core;

/// Фильтр ответа модели запятых: берём только безопасное.
/// Запятые и знаки — да; замену слова — только если её принял бы исправлятор опечаток (или е→ё, дефис частицы);
/// добавленные и удалённые слова — никогда; точку между цифрами — никогда;
/// заглавную в начале и точку в конце — только если `capitalize`. Перенос `Sources/ValloCore/CommaFilter.swift`.
public static class CommaFilter
{
    const string AllowedMarks = ",?!:;";
    static readonly Regex DotBetweenDigits = new(@"(\d)\.\s+(\d)", RegexOptions.Compiled);

    static bool IsWord(char c) => char.IsLetter(c) || char.IsNumber(c);

    public static string Merge(string original, string model, TypoCorrector typos, bool capitalize)
    {
        // слова человека вместе с пробелами между ними — пробелы, переносы и отступы остаются как были
        var (seps, ot) = Tokenize(original);
        var mt = model.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (ot.Count == 0) return original;
        var output = new List<string>(ot);

        foreach (var op in Diff(ot.Select(Key).ToList(), mt.Select(Key).ToList()))
        {
            switch (op)
            {
                case Op.Equal(var i, var j):
                    output[i] = SameWord(ot[i], mt[j]);
                    break;
                case Op.Replace(var i1, var i2, var j1, var j2):
                    var replaced = Replace(ot.GetRange(i1, i2 - i1), mt.GetRange(j1, j2 - j1), typos);
                    for (var k = 0; k < replaced.Count; k++) output[i1 + k] = replaced[k];
                    break;
                // добавленные моделью слова не берём, удалённые — оставляем свои
            }
        }

        // тире после слова поставил человек — запятая модели перед ним лишняя: «по резюме, — не покажет»
        for (var i = 0; i < output.Count - 1; i++)
            if (IsDash(ot[i + 1]) && !Trail(ot[i]).Contains(',') && output[i].EndsWith(','))
                output[i] = output[i][..^1];

        // собрать: пустое слово (склеенная частица) забирает с собой пробел перед ним
        var body = new StringBuilder(seps[0]);
        for (var i = 0; i < output.Count; i++)
            if (output[i].Length > 0) body.Append(i == 0 ? "" : seps[i]).Append(output[i]);
        var text = DotBetweenDigits.Replace(body.ToString(), "$1 $2");
        // запятая в конце, оставшаяся от отброшенного слова
        while (text.EndsWith(',') && !original.Trim().EndsWith(',')) text = text[..^1];
        if (capitalize)
        {
            var first = text.ToList().FindIndex(char.IsLetter);
            if (first >= 0) text = text[..first] + char.ToUpperInvariant(text[first]) + text[(first + 1)..];
            if (text.Length > 0 && !".!?…".Contains(text[^1])) text += ".";
        }
        return text + seps[ot.Count];
    }

    /// Отдельно стоящее тире или дефис («—», «–», «-», «--»).
    static bool IsDash(string t) => t.Length > 0 && t.All(c => "—–-".Contains(c));

    /// Слова и пробелы между ними: `seps[i]` — пробелы перед словом `i`, `seps[n]` — хвост.
    internal static (List<string> Seps, List<string> Words) Tokenize(string text)
    {
        List<string> seps = [], words = [];
        var sep = new StringBuilder();
        var word = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (word.Length > 0) { words.Add(word.ToString()); word.Clear(); }
                sep.Append(ch);
            }
            else
            {
                if (word.Length == 0) { seps.Add(sep.ToString()); sep.Clear(); }
                word.Append(ch);
            }
        }
        if (word.Length > 0) words.Add(word.ToString());
        seps.Add(sep.ToString());
        return (seps, words);
    }

    // Слова

    /// Ключ сравнения: без знаков по краям, строчные, ё → е, без дефиса.
    static string Key(string t) => Lexicon.Normalize(Core(t)).Replace("-", "");

    static string Core(string t)
    {
        var first = -1;
        for (var i = 0; i < t.Length; i++) if (IsWord(t[i])) { first = i; break; }
        if (first < 0) return "";
        var last = first;
        for (var i = t.Length - 1; i >= first; i--) if (IsWord(t[i])) { last = i; break; }
        return t[first..(last + 1)];
    }

    static int LastWordChar(string t)
    {
        for (var i = t.Length - 1; i >= 0; i--) if (IsWord(t[i])) return i;
        return -1;
    }

    /// Знаки после слова у модели — только разрешённые.
    static string Marks(string t)
    {
        var last = LastWordChar(t);
        return last < 0 ? "" : new string(t[(last + 1)..].Where(c => AllowedMarks.Contains(c)).ToArray());
    }

    /// Начало слова у человека (знаки перед словом сохраняем как были).
    static string Lead(string t) => new(t.TakeWhile(c => !IsWord(c)).ToArray());

    /// Регистр первой буквы — как у человека.
    static string WithLeadingCase(string original, string word)
    {
        var o = Core(original);
        if (o.Length == 0 || word.Length == 0) return word;
        return (char.IsUpper(o[0]) ? char.ToUpperInvariant(word[0]) : char.ToLowerInvariant(word[0])) + word[1..];
    }

    /// Знаки после слова у человека: «.», «»», «)», «...» — всегда его.
    static string Trail(string t)
    {
        var last = LastWordChar(t);
        return last < 0 ? t : t[(last + 1)..];
    }

    /// К своим знакам человека добавляем знак модели, только если у человека знака препинания нет.
    /// Запятая модели встаёт после закрывающей кавычки или скобки: «да», и ушёл.
    static string OwnTrail(string original, string modelMarks)
    {
        var own = Trail(original);
        return own.Any(c => ",.?!:;…".Contains(c)) ? own : own + modelMarks;
    }

    /// То же слово: знаки — свои плюс запятая модели; написание — модели, если отличается дефисом.
    /// «ё» модели не берём: её ставит наш словарь (свой переключатель, двойные «все/всё» не трогаем, ё → е никогда).
    static string SameWord(string original, string model)
    {
        // отдельный знак без букв (тире, «...», «→») — как у человека: иначе «—» склеивался сам с собой в «——»
        if (Core(original).Length == 0) return original;
        string o = Core(original), m = Core(model);
        var word = Lexicon.Normalize(o) == Lexicon.Normalize(m) ? o : WithLeadingCase(original, m);
        return Lead(original) + word + OwnTrail(original, Marks(model));
    }

    static List<string> Replace(List<string> o, List<string> m, TypoCorrector typos)
    {
        // «какой то» → «какой-то»
        if (o.Count == 2 && m.Count == 1 && TypoCorrector.Particles.Contains(Lexicon.Normalize(Core(o[1])))
            && TypoCorrector.ParticleStems.Contains(Lexicon.Normalize(Core(o[0])))
            && Key(m[0]) == Key(o[0]) + Key(o[1]))
            return [Lead(o[0]) + WithLeadingCase(o[0], Core(m[0])) + OwnTrail(o[1], Marks(m[0])), ""];
        // замена слово на слово — только если исправлятор опечаток согласен
        if (o.Count == m.Count)
            return o.Zip(m, (ow, mw) =>
                typos.Correct(Core(ow)) is { } fixedWord && Key(fixedWord) == Key(mw)
                    ? Lead(ow) + fixedWord + OwnTrail(ow, Marks(mw))
                    : Lead(ow) + Core(ow) + OwnTrail(ow, CommaOnly(mw))).ToList();
        // модель разбила или склеила слова — слова оставляем свои, берём только запятую после куска
        if (o.Count == 0 || m.Count == 0) return o;
        var last = o[^1];
        return [.. o.Take(o.Count - 1), Lead(last) + Core(last) + OwnTrail(last, CommaOnly(m[^1]))];
    }

    /// От отвергнутого слова модели берём только запятую: «СП:» вместо «спс» не должен оставить двоеточие.
    static string CommaOnly(string t) => Marks(t).Contains(',') ? "," : "";

    // Выравнивание слов

    abstract record Op
    {
        public sealed record Equal(int I, int J) : Op;
        public sealed record Replace(int I1, int I2, int J1, int J2) : Op;
        public sealed record Insert(int J1, int J2) : Op;
        public sealed record Delete(int I1, int I2) : Op;
    }

    /// Выравнивание двух списков по наибольшей общей подпоследовательности; несовпавшие куски —
    /// замена, вставка или удаление.
    static List<Op> Diff(List<string> a, List<string> b)
    {
        int n = a.Count, m = b.Count;
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        var ops = new List<Op>();
        int x = 0, y = 0, gapA = 0, gapB = 0;
        void FlushGap()
        {
            int i1 = x - gapA, j1 = y - gapB;
            if (gapA > 0 && gapB > 0) ops.Add(new Op.Replace(i1, x, j1, y));
            else if (gapA > 0) ops.Add(new Op.Delete(i1, x));
            else if (gapB > 0) ops.Add(new Op.Insert(j1, y));
            gapA = 0; gapB = 0;
        }
        while (x < n || y < m)
        {
            if (x < n && y < m && a[x] == b[y])
            {
                FlushGap();
                ops.Add(new Op.Equal(x, y));
                x++; y++;
            }
            else if (y < m && (x == n || lcs[x, y + 1] >= lcs[x + 1, y])) { y++; gapB++; }
            else { x++; gapA++; }
        }
        FlushGap();
        return ops;
    }
}
