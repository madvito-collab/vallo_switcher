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
}
