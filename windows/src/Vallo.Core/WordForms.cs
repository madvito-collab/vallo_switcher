namespace Vallo.Core;

/// Список слов с формами в скобках: `чат(|а|у)` = чат, чата, чату. «#» — комментарий.
/// Так хранится `ru-modern.txt` — современные слова, которых нет в словарях субтитров и Википедии.
public static class WordForms
{
    public static List<string> Load(string path) =>
        File.ReadAllText(path).Split('\n').SelectMany(Expand).ToList();

    public static List<string> Expand(string line)
    {
        var entry = line.Split('#', 2)[0].Trim(' ', '\t', '\r');
        if (entry.Length == 0) return new List<string>();
        int open = entry.IndexOf('(');
        if (open < 0 || !entry.EndsWith(')')) return new List<string> { entry };
        var stem = entry[..open];
        var endings = entry[(open + 1)..^1];
        return endings.Split('|').Select(e => stem + e).ToList();
    }
}
