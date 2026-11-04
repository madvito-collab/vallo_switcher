namespace Vallo.Core;

/// Словарь одного языка: слова, все их начала и статистика сочетаний из трёх букв.
public sealed class Lexicon
{
    readonly HashSet<string> words = new();
    readonly HashSet<string> prefixes = new();
    readonly Dictionary<string, int> trigrams = new();
    readonly int trigramTotal;

    public Lexicon(IEnumerable<string> list)
    {
        int total = 0;
        foreach (var raw in list)
        {
            var w = Normalize(raw);
            if (w.Length == 0 || !w.All(char.IsLetter)) continue;
            words.Add(w);
            for (int i = 1; i <= w.Length; i++) prefixes.Add(w[..i]);
            foreach (var tri in TrigramsOf(w))
            {
                trigrams[tri] = trigrams.GetValueOrDefault(tri) + 1;
                total++;
            }
        }
        trigramTotal = total;
    }

    /// Формат файла: «слово количество» на строку, самые частые сверху.
    /// Берём только слова алфавита языка. 20 000 — хвост списков шумный.
    /// `extra` — свои слова сверх списка (современные слова из `ru-modern.txt`).
    public static Lexicon Load(string path, Language language, int limit = 20_000, IEnumerable<string>? extra = null)
    {
        var list = new List<string>(limit);
        foreach (var line in File.ReadAllText(path).Split('\n'))
        {
            if (list.Count >= limit) break;
            var word = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim('\r');
            if (string.IsNullOrEmpty(word)) continue;
            if (!word.ToLowerInvariant().All(c => IsInAlphabet(c, language))) continue;
            list.Add(word);
        }
        var rules = SwitcherRules.Shared;
        // частые ошибки — тоже русские слова, иначе свитчер примет «воб» за английский
        if (language == Language.Ru) list.AddRange(rules.Misspellings.Keys);
        // частые ё-слова, которых нет в списке субтитров
        if (language == Language.Ru) list.AddRange(rules.ExtraRussianWords);
        // аббревиатуры — слова своего языка
        list.AddRange(rules.Abbreviations[language]);
        if (extra != null) list.AddRange(extra);
        return new Lexicon(list);
    }

    public bool IsWord(string s) => words.Contains(Normalize(s));

    /// Любое начало любого слова, включая само слово.
    public bool IsPrefix(string s) => prefixes.Contains(Normalize(s));

    /// Насколько строка похожа на слова этого языка: средний log P триграмм
    /// с границами "^^s$", сглаживание add-one. Больше — похожее.
    public double TrigramScore(string s)
    {
        var tris = TrigramsOf(Normalize(s));
        if (tris.Count == 0) return 0;
        double denom = trigramTotal + trigrams.Count + 1;
        double sum = 0;
        foreach (var t in tris) sum += Math.Log((trigrams.GetValueOrDefault(t) + 1) / denom);
        return sum / tris.Count;
    }

    public static bool IsInAlphabet(char ch, Language language) => language switch
    {
        Language.En => ch >= 'a' && ch <= 'z',
        _ => (ch >= 'а' && ch <= 'я') || ch == 'ё',
    };

    public static string Normalize(string s) => s.ToLowerInvariant().Replace('ё', 'е');

    static List<string> TrigramsOf(string w)
    {
        var chars = "^^" + w + "$";
        var result = new List<string>();
        for (int i = 0; i + 3 <= chars.Length; i++) result.Add(chars.Substring(i, 3));
        return result;
    }
}
