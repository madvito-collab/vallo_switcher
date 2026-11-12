using System.Text.RegularExpressions;

namespace Vallo.Core;

/// Исправлятор опечаток: только промахи пальцев (лишняя, пропущенная, переставленная, задвоенная буква)
/// на словаре частых слов. Сленг, жаргон, редкие правильные слова и «Не исправлять слова» не трогает.
/// Регистр не меняет никогда. Перенос `Sources/ValloCore/TypoCorrector.swift` — правила и пороги те же.
public sealed class TypoCorrector
{
    /// Кандидаты в исправление — только из первых `CandidateLimit` слов.
    public const int CandidateLimit = 40_000;
    public const int MinLength = 5;
    /// Частоты в словаре — на миллион слов × 1000 (см. scripts/build-typo-dictionary.py); 500 = 0,5 на миллион.
    const int MinCandidateFrequency = 500;
    const int ConfidenceRatio = 10;
    const int RareWordRatio = 300;
    const string Alphabet = "абвгдежзийклмнопрстуфхцчшщъыьэюя";

    /// Частицы, которые пишутся через дефис, и слова, после которых они частицы, а не союз «то».
    public static readonly string[] Particles = ["то", "нибудь", "либо", "таки"];
    public static readonly HashSet<string> ParticleStems =
    [
        "кто", "что", "какой", "какая", "какое", "какие", "какого", "какому", "каким", "каком", "какую", "какими", "каких",
        "где", "куда", "когда", "как", "почему", "зачем", "откуда", "сколько", "чей", "чья", "чье", "чьи",
        "кого", "кому", "кем", "ком", "чего", "чему", "чем",
    ];

    /// На лету после этих слов «то» часто местоимение («что то решение», «как то, так и другое») — не склеиваем.
    static readonly HashSet<string> AmbiguousStems = ["что", "как", "чем"];
    static readonly Regex ParticleRegex = new(@"(?i)(?<![\p{L}])(\p{L}+) (то|нибудь|либо|таки)(?![\p{L}])", RegexOptions.Compiled);

    /// Частые ошибки «как слышится» — то, что поиск промахов не ловит. Точное совпадение, ключи нормализованы (ё → е).
    /// Список — `Data/switcher-rules.json`.
    static IReadOnlyDictionary<string, string> Misspellings => SwitcherRules.Shared.Misspellings;

    /// «Не исправлять слова» — нормализованы (строчные, ё → е).
    public IReadOnlySet<string> Ignored { get; set; }
    /// Словарь «ё»: настоящие слова, которых нет в словаре опечаток («введем») — не опечатка.
    /// Работает и при выключенной расстановке «ё».
    public Yoficator? YoWords { get; set; }
    /// Современные слова (`ru-modern.txt`: чатах, дедлайны) — нормализованы. Без них «чатах» → «чартах».
    public IReadOnlySet<string> ModernWords { get; set; } = new HashSet<string>();
    /// Все формы русских слов (OpenCorpora, `ru-forms.txt`): правильное слово не трогаем, даже если оно редкое.
    /// Слова, которые бывают только именами, записаны со «*»: их не трогаем, но опечатку в них не превращаем.
    public SortedWordFile? Forms { get; set; }

    bool IsForm(string w) => Forms is { } f && (f.Contains(w) || f.Contains(w + "*"));

    readonly List<string> order = new();                    // слова по убыванию частоты
    readonly Dictionary<string, int> frequency = new();      // все слова словаря
    readonly Dictionary<string, string> display = new();     // как писать слово (с «ё», если в словаре оно с ё)
    readonly Dictionary<string, int> rankIndex = new();      // место слова в словаре

    public TypoCorrector(IEnumerable<(string Word, int Count)> frequencies, IEnumerable<string> ignored)
    {
        foreach (var (raw, count) in frequencies)
        {
            var w = Lexicon.Normalize(raw);
            if (frequency.ContainsKey(w)) continue;
            rankIndex[w] = order.Count;
            order.Add(w);
            frequency[w] = count;
            display[w] = raw.ToLowerInvariant();
        }
        Ignored = ignored.Select(Lexicon.Normalize).ToHashSet();
    }

    /// Формат как у словаря свитчера: «слово количество», самые частые сверху.
    /// Словарь — `Data/wordlists/ru-typos.txt`: разговорные слова (субтитры) + письменные (Википедия), 150 000 слов.
    public static TypoCorrector Load(string path, IEnumerable<string> ignored, int limit = 150_000)
    {
        var list = new List<(string, int)>(limit);
        foreach (var line in File.ReadLines(path))
        {
            if (list.Count >= limit) break;
            var parts = line.TrimEnd('\r').Split(' ');
            if (parts.Length != 2 || !int.TryParse(parts[1], out var count)) continue;
            var word = parts[0];
            if (!word.ToLowerInvariant().All(c => Lexicon.IsInAlphabet(c, Language.Ru))) continue;
            list.Add((word, count));
        }
        return new TypoCorrector(list, ignored);
    }

    /// Слово среди частых — точно не опечатка.
    public bool IsKnown(string word) =>
        rankIndex.TryGetValue(Lexicon.Normalize(word), out var r) && r < CandidateLimit;

    /// Слова словаря с места `from` по `to` (для тестов).
    public IReadOnlyList<string> WordsRange(int from, int to) =>
        order.GetRange(Math.Min(from, order.Count), Math.Min(to, order.Count) - Math.Min(from, order.Count));

    // Исправление

    static bool AllRussian(string w) => w.All(c => Lexicon.IsInAlphabet(c, Language.Ru));

    /// Исправленное слово или null — не трогать.
    public string? Correct(string word)
    {
        var w = Lexicon.Normalize(word);
        // смешанный регистр («пРвет») — не трогаем: регистр не меняем никогда
        var letters = word.Where(char.IsLetter).ToList();
        if (letters.Skip(1).Any(char.IsUpper) && !letters.All(char.IsUpper)) return null;
        if (Misspellings.TryGetValue(w, out var fixedSpelling))
            return Ignored.Contains(w) ? null : WithCase(word, fixedSpelling);
        // забытый «ь» в конце: «можеш» → «можешь» — узкое исключение из «окончания не трогаем»
        if (w.Length >= 4 && w.EndsWith('ш') && AllRussian(w)
            && !frequency.ContainsKey(w) && !Ignored.Contains(w) && !IsForm(w) && IsKnown(w + "ь"))
            return WithCase(word, display.GetValueOrDefault(w + "ь", w + "ь"));
        if (w.Length < MinLength || !AllRussian(w) || IsKnown(w) || Ignored.Contains(w) || Judge.IsSlang(w, Language.Ru)
            || YoWords?.Contains(w) == true || ModernWords.Contains(w) || IsForm(w)) return null;
        if (Hyphenate(w) is { } hyphenated) return WithCase(word, hyphenated);

        var own = frequency.GetValueOrDefault(w);
        // со словарём форм правильное слово сюда не дойдёт — можно смелее: и окончания, и гласные, и редкие слова
        var full = Forms is not null;
        var candidates = (full ? FullEdits(w) : Edits(w))
            // со словарём форм — только настоящие нарицательные слова: не «силла», не «тамбов»
            .Where(c => c.Length >= MinLength && (full ? frequency.ContainsKey(c) && (Forms!.Contains(c) || ModernWords.Contains(c)) : IsKnown(c)))
            // по первой букве почти не промахиваются, а жаргон так портится: «ассеты» → «кассеты»
            .Where(c => c[0] == w[0])
            .OrderByDescending(c => frequency[c])
            .ToList();
        if (candidates.Count == 0) return null;
        var best = candidates[0];
        var bestFreq = frequency[best];
        if (bestFreq < MinCandidateFrequency) return null;
        if (candidates.Count > 1 && bestFreq < ConfidenceRatio * frequency[candidates[1]]) return null;
        // редкое, но настоящее слово трогаем, только если рядом в сотни раз более частое
        if (own > 0 && bestFreq < RareWordRatio * own) return null;
        return WithCase(word, display.GetValueOrDefault(best, best));
    }

    /// Все слова фразы + дефисы частиц («какой то» → «какой-то»).
    public string CorrectPhrase(string text) => JoinParticles(Words.Map(text, w => Correct(w) ?? w));

    /// «какой то» → «какой-то», только после местоимений и наречий (иначе «то» — союз).
    /// `live` — на лету: «что/как/чем то» не склеиваем, там «то» часто местоимение.
    public string JoinParticles(string text, bool live = false)
    {
        var result = text;
        foreach (var m in ParticleRegex.Matches(text).Reverse())
        {
            var stem = Lexicon.Normalize(m.Groups[1].Value);
            if (!ParticleStems.Contains(stem) || (live && AmbiguousStems.Contains(stem))) continue;
            result = result[..m.Index] + result.Substring(m.Index, m.Length).Replace(' ', '-') + result[(m.Index + m.Length)..];
        }
        return result;
    }

    /// «ктото» → «кто-то».
    static string? Hyphenate(string w)
    {
        foreach (var p in Particles)
        {
            if (!w.EndsWith(p, StringComparison.Ordinal)) continue;
            var stem = w[..^p.Length];
            if (ParticleStems.Contains(stem)) return stem + "-" + p;
        }
        return null;
    }

    static string Remove(string w, int i) => w.Remove(i, 1);
    static string Swap(string w, int i) { var a = w.ToCharArray(); (a[i], a[i + 1]) = (a[i + 1], a[i]); return new string(a); }
    static string Insert(string w, int i, char ch) => w.Insert(i, ch.ToString());
    static string Replace(string w, int i, char ch) { var a = w.ToCharArray(); a[i] = ch; return new string(a); }

    /// Варианты на один промах. Последнюю букву не убираем (там окончание: «кейсы» → «кейс» — не опечатка),
    /// пропущенную не вставляем прямо перед последней; переставлять можно и две последние («привте»).
    static HashSet<string> Edits(string w)
    {
        var out_ = new HashSet<string>();
        var n = w.Length;
        for (var i = 0; i < n - 1; i++)
        {
            out_.Add(Remove(w, i));
            out_.Add(Swap(w, i));
            foreach (var ch in Alphabet) out_.Add(Insert(w, i, ch));
        }
        out_.Remove(w);
        return out_;
    }

    /// Гласные, которые путают на слух: «большенстве» → «большинстве», «содержить» → «содержать».
    static readonly Dictionary<char, char[]> VowelSwaps = new()
    {
        ['е'] = ['и', 'я', 'э'], ['и'] = ['е', 'ы'], ['а'] = ['о', 'я'], ['о'] = ['а'], ['я'] = ['е', 'а'], ['ы'] = ['и'], ['э'] = ['е'],
    };

    /// С какой длины слова пробуем соседнюю клавишу (подобрано по `RealTyposTests`).
    const int NeighborMinLength = 7;

    /// Ряды клавиатуры ЙЦУКЕН — соседние клавиши для «толстого пальца».
    static readonly string[] Rows = ["йцукенгшщзхъ", "фывапролджэ", "ячсмитьбю"];
    static readonly Dictionary<char, HashSet<char>> Neighbors = BuildNeighbors();

    static Dictionary<char, HashSet<char>> BuildNeighbors()
    {
        var n = new Dictionary<char, HashSet<char>>();
        void Link(char a, char b)
        {
            (n.TryGetValue(a, out var sa) ? sa : n[a] = []).Add(b);
            (n.TryGetValue(b, out var sb) ? sb : n[b] = []).Add(a);
        }
        for (var r = 0; r < Rows.Length; r++)
        {
            var row = Rows[r];
            for (var i = 0; i < row.Length; i++)
            {
                if (i + 1 < row.Length) Link(row[i], row[i + 1]);
                // ряд ниже сдвинут вправо: клавиша соседствует с двумя над собой
                if (r > 0)
                {
                    var up = Rows[r - 1];
                    if (i < up.Length) Link(row[i], up[i]);
                    if (i + 1 < up.Length) Link(row[i], up[i + 1]);
                }
            }
        }
        return n;
    }

    static bool AreNeighbors(char a, char b) => Neighbors.TryGetValue(a, out var s) && s.Contains(b);

    /// Со словарём форм: промахи в любом месте слова, соседняя клавиша, путаница гласных. Конец слова — осторожно, там грамматика:
    /// последнюю букву убираем, только если это соседняя клавиша или повтор («самыен», «невероятныхз»),
    /// дописываем в конец только «ь» («тепер»), гласные в двух последних буквах не меняем («тестим» ≠ «тестем»).
    static HashSet<string> FullEdits(string w)
    {
        var out_ = new HashSet<string>();
        var n = w.Length;
        for (var i = 0; i < n; i++)
        {
            var last = i == n - 1;
            if (!last || (n > 1 && (w[i] == w[i - 1] || AreNeighbors(w[i - 1], w[i])))) out_.Add(Remove(w, i));
            if (i < n - 1) out_.Add(Swap(w, i));
            if (i < n - 2 && VowelSwaps.TryGetValue(w[i], out var swaps))
                foreach (var ch in swaps) out_.Add(Replace(w, i, ch));
            // промах по соседней клавише: «ошидкой» → «ошибкой» (д и б рядом); первую букву не трогаем.
            // Только в длинных словах: короткий сленг иначе портится («короч» → «коров», «могет» → «монет»)
            if (i > 0 && n >= NeighborMinLength && Neighbors.TryGetValue(w[i], out var near))
                foreach (var ch in near) out_.Add(Replace(w, i, ch));
        }
        for (var i = 0; i < n; i++)
            foreach (var ch in Alphabet) out_.Add(Insert(w, i, ch));
        out_.Add(w + "ь");
        out_.Remove(w);
        return out_;
    }

    /// Регистр как у набранного: «Првет» → «Привет», «ПРВЕТ» → «ПРИВЕТ». Общий с `Yoficator`.
    public static string WithCase(string original, string fixedWord)
    {
        var letters = original.Where(char.IsLetter).ToList();
        if (letters.Count > 1 && letters.All(char.IsUpper)) return fixedWord.ToUpperInvariant();
        if (original.Length > 0 && char.IsUpper(original[0]) && fixedWord.Length > 0)
            return char.ToUpperInvariant(fixedWord[0]) + fixedWord[1..];
        return fixedWord;
    }
}
