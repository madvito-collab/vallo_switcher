using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class LayoutFixtureTests
{
    static string ViaRu(string typedEn)
    {
        var pair = TestSupport.FixturePair();
        var strokes = TestSupport.StrokesTyping(typedEn, (TableKeyLayout)pair.Layout(Language.En));
        return string.Concat(strokes.Select(s => pair.Layout(Language.Ru).Character(s)));
    }

    [Fact]
    public void FixturesRenderPrivet() => Assert.Equal("привет", ViaRu("ghbdtn"));

    [Fact]
    public void YoOnBacktick()
    {
        var pair = TestSupport.FixturePair();
        var en = (TableKeyLayout)pair.Layout(Language.En);
        var ru = (TableKeyLayout)pair.Layout(Language.Ru);
        var k = new KeyStroke(0xC0);
        Assert.Equal("`", en.Character(k));
        Assert.Equal("ё", ru.Character(k));
        Assert.Contains(ru.Table, kv => kv.Value == "Ё");
    }

    [Fact]
    public void BracketsCommaDotAreLetters() => Assert.Equal("хъжэбю", ViaRu("[];',."));

    [Fact]
    public void QAndYSameKey()
    {
        var pair = TestSupport.FixturePair();
        var k = new KeyStroke(0x51);   // VK_Q
        Assert.Equal("q", pair.Layout(Language.En).Character(k));
        Assert.Equal("й", pair.Layout(Language.Ru).Character(k));
    }

    [Fact]
    public void CommaAndDotKeys()
    {
        var pair = TestSupport.FixturePair();
        Assert.Equal("б", pair.Layout(Language.Ru).Character(new KeyStroke(0xBC)));
        Assert.Equal("ю", pair.Layout(Language.Ru).Character(new KeyStroke(0xBE)));
    }

    [Fact]
    public void CaseByShiftAndCaps()
    {
        var ru = TestSupport.FixturePair().Layout(Language.Ru);
        Assert.Equal("й", ru.Character(new KeyStroke(0x51)));
        Assert.Equal("Й", ru.Character(new KeyStroke(0x51, true)));
        Assert.Equal("Й", ru.Character(new KeyStroke(0x51, false, true)));
        Assert.Equal("й", ru.Character(new KeyStroke(0x51, true, true)));
    }
}
