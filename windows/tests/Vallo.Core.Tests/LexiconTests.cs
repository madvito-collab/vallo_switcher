using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class LexiconTests
{
    [Fact]
    public void PrefixesAndWords()
    {
        var lex = new Lexicon(new[] { "привет", "при", "hello" });
        Assert.True(lex.IsPrefix("пр"));
        Assert.True(lex.IsPrefix("при"));
        Assert.False(lex.IsPrefix("прф"));
        Assert.True(lex.IsWord("при"));
        Assert.False(lex.IsWord("прив"));
    }

    [Fact]
    public void YoNormalized()
    {
        var lex = new Lexicon(new[] { "еж" });
        Assert.True(lex.IsWord("ёж"));
        Assert.True(lex.IsWord("ЁЖ"));
    }

    [Fact]
    public void RealLists()
    {
        var en = TestSupport.RealEN;
        var ru = TestSupport.RealRU;
        Assert.True(ru.IsWord("привет"));
        Assert.True(ru.IsWord("будет"));
        Assert.True(en.IsWord("hello"));
        Assert.False(en.IsPrefix("ghb"));
        Assert.True(ru.IsPrefix("при"));
        Assert.True(ru.TrigramScore("привет") > ru.TrigramScore("ghbdtn"));
        Assert.True(en.TrigramScore("hello") > en.TrigramScore("руддщ"));
        Assert.False(ru.IsWord("the"));   // английское из русского списка отфильтровано
    }

    [Fact]
    public void LoadAddsSharedRules()
    {
        var ru = TestSupport.RealRU;
        Assert.True(ru.IsWord("вобщем"));    // частые ошибки
        Assert.True(ru.IsWord("сожжен"));    // extraRussianWords
        Assert.True(TestSupport.RealEN.IsWord("pdf"));   // аббревиатуры
    }

    [Fact]
    public void TrigramScoreMatchesSwift()
    {
        // значения сняты в Swift: Lexicon(words:).trigramScore, %.12f
        var lex = new Lexicon(new[] { "привет", "при", "приветствие", "ветер", "вечер", "привычка", "ответ", "Ёжик", "hello" });
        Assert.Equal(-3.212780815114, lex.TrigramScore("привет"), 9);
        Assert.Equal(-4.644390899141, lex.TrigramScore("ghbdtn"), 9);
        Assert.Equal(-4.182292778768, lex.TrigramScore("ёж"), 9);
    }

    [Fact]
    public void MeasureLoad()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Lexicon.Load(TestSupport.RepoData("wordlists/en.txt"), Language.En);
        Lexicon.Load(TestSupport.RepoData("wordlists/ru.txt"), Language.Ru);
        Assert.True(sw.Elapsed.TotalSeconds < 3);
    }
}
