using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class SwitcherRulesTests
{
    [Fact]
    public void RulesLoadFromData()
    {
        var rules = SwitcherRules.Shared;
        Assert.Contains("ок", rules.ShortWords[Language.Ru]);
        Assert.Contains("pdf", rules.Abbreviations[Language.En]);
        Assert.Equal("в общем", rules.Misspellings["вобщем"]);
        Assert.Contains("руб", rules.UnitsAfterNumber[Language.Ru]);
        Assert.Contains("сожжен", rules.ExtraRussianWords);
        Assert.Equal(new[] { "тыс", "млн", "млрд" }, rules.UnitsBeforeUnit);
    }
}
