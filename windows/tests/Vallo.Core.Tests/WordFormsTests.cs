using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class WordFormsTests
{
    [Fact]
    public void ExpandsForms()
    {
        Assert.Equal(new[] { "чат", "чата", "чату" }, WordForms.Expand("чат(|а|у)"));
        Assert.Equal(new[] { "фича", "фичи", "фич" }, WordForms.Expand("фич(а|и|)"));
        Assert.Equal(new[] { "эмодзи" }, WordForms.Expand("эмодзи"));
        Assert.Empty(WordForms.Expand("  # комментарий"));
        Assert.Equal(new[] { "сторис" }, WordForms.Expand("сторис # в Инстаграме"));
    }

    [Fact]
    public void ModernListLoads()
    {
        var modern = WordForms.Load(TestSupport.RepoData("wordlists/ru-modern.txt"));
        Assert.Contains("чатах", modern);
        Assert.Contains("дедлайны", modern);
        Assert.DoesNotContain(modern, w => w.Contains('(') || w.Contains('|') || w.Contains(' '));
    }

    [Fact]
    public void SwitcherLeavesModernWords()
    {
        // как с «вобщем»: слово, которого свитчер не знает, он может перевести посреди набора
        var modern = WordForms.Load(TestSupport.RepoData("wordlists/ru-modern.txt"));
        var ru = Lexicon.Load(TestSupport.RepoData("wordlists/ru.txt"), Language.Ru, extra: modern);
        var judge = new Judge(TestSupport.RealEN, ru, new HashSet<string>());
        var failed = new List<string>();
        foreach (var word in modern)
        {
            var engine = new Engine(EngineSupport.Win, judge, Language.Ru);
            if (TestSupport.StrokesTyping(word + " ", EngineSupport.WinRu)
                .Any(s => engine.HandleKey(new KeyEvent(s, false)) != EngineOutput.PassThrough))
                failed.Add(word);
        }
        Assert.True(failed.Count == 0, $"свитчер мешает: {string.Join(", ", failed)}");
    }
}
