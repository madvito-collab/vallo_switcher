namespace Vallo.Core;

/// Расстановка «ё»: только однозначные слова («еще» → «ещё», «Шмелев» → «Шмелёв»).
/// Двойные (все/всё) в словаре нет — сомневаешься, не ставь. «ё» → «е» не делаем никогда.
/// Словарь — `Data/wordlists/ru-yo.txt` из eyo-kernel (MIT). Перенос `Sources/ValloCore/Yoficator.swift`.
public sealed class Yoficator
{
    /// Слово через «е» (нормализованное) → как писать с «ё» и имя ли это собственное.
    readonly Dictionary<string, (string Spelling, bool Proper)> words = new();

    /// Формы с «ё», как в словаре: с заглавной — имя собственное (ставим, только если набрано с заглавной).
    public Yoficator(IEnumerable<string> list)
    {
        foreach (var raw in list)
        {
            if (raw.Length == 0) continue;
            words[Lexicon.Normalize(raw)] = (raw.ToLowerInvariant(), char.IsUpper(raw[0]));
        }
    }

    public static Yoficator Load(string path) =>
        new(File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')));

    /// Слово есть в словаре (через «е» или «ё», любой регистр).
    public bool Contains(string word) => words.ContainsKey(Lexicon.Normalize(word));

    /// Слово с «ё» или null — не трогать.
    public string? Fix(string word)
    {
        if (word.Contains('ё') || word.Contains('Ё')) return null;
        if (!words.TryGetValue(Lexicon.Normalize(word), out var entry)) return null;
        var letters = word.Where(char.IsLetter).ToList();
        // смешанный регистр («еЩе») — не трогаем, как у опечаток
        if (letters.Skip(1).Any(char.IsUpper) && !letters.All(char.IsUpper)) return null;
        if (entry.Proper && !(word.Length > 0 && char.IsUpper(word[0]))) return null;
        return TypoCorrector.WithCase(word, entry.Spelling);
    }

    /// Все слова текста (правый Shift по выделенному).
    public string FixPhrase(string text) => Words.Map(text, w => Fix(w) ?? w);
}

/// Разбор текста на слова (подряд идущие буквы) — общий для «ё» и опечаток.
static class Words
{
    public static string Map(string text, Func<string, string> map)
    {
        var out_ = new System.Text.StringBuilder(text.Length);
        var word = new System.Text.StringBuilder();
        void Flush()
        {
            if (word.Length > 0) out_.Append(map(word.ToString()));
            word.Clear();
        }
        foreach (var ch in text)
        {
            if (char.IsLetter(ch)) word.Append(ch);
            else { Flush(); out_.Append(ch); }
        }
        Flush();
        return out_.ToString();
    }
}
