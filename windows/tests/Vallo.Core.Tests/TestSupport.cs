using Vallo.Core;

namespace Vallo.Core.Tests;

static class TestSupport
{
    /// Путь внутри Data/ репозитория (тестовый проект копирует Data/** в вывод).
    public static string RepoData(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "Data", relative.StartsWith("Data/") ? relative[5..] : relative);

    /// Раскладки EN (win-us) и RU (win-ru), снятые с Windows.
    public static LayoutPair FixturePair() => new(
        TableKeyLayout.Load(RepoData("layouts/win-us.json")),
        TableKeyLayout.Load(RepoData("layouts/win-ru.json")));

    /// Нажатия, которые в раскладке дают этот текст (без CapsLock); при нескольких клавишах — меньший VK, но цифровая клавиатура (VK 0x60–0x6F) хуже основного блока.
    public static List<KeyStroke> StrokesTyping(string text, TableKeyLayout layout) =>
        text.Select(ch => layout.Table
            .Where(kv => kv.Value == ch.ToString() && !kv.Key.CapsLock)
            .Select(kv => kv.Key)
            .MinBy(k => (Converter.IsNumpad(k), k.KeyCode))).Select(k => k).ToList();

    public static Lexicon RealEN = Lexicon.Load(RepoData("wordlists/en.txt"), Language.En);
    public static Lexicon RealRU = Lexicon.Load(RepoData("wordlists/ru.txt"), Language.Ru);
}
