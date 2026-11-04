using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class ConverterTests
{
    static string Conv(string text, Language from, Language to)
    {
        var pair = TestSupport.FixturePair();
        return Converter.Convert(text, pair.Layout(from), pair.Layout(to));
    }

    [Fact]
    public void EnglishTypedRussian()
    {
        Assert.Equal("привет", Conv("ghbdtn", Language.En, Language.Ru));
        Assert.Equal("будет", Conv(",eltn", Language.En, Language.Ru));
        Assert.Equal("привет как дела", Conv("ghbdtn rfr ltkf", Language.En, Language.Ru));
    }

    [Fact]
    public void CasePreserved()
    {
        Assert.Equal("Привет", Conv("Ghbdtn", Language.En, Language.Ru));
        Assert.Equal("ПРИВЕТ", Conv("GHBDTN", Language.En, Language.Ru));
    }

    [Fact]
    public void RussianTypedEnglish() => Assert.Equal("hello", Conv("руддщ", Language.Ru, Language.En));

    [Fact]
    public void YoRoundTrip()
    {
        var en = Conv("ёжик", Language.Ru, Language.En);
        Assert.Equal("ёжик", Conv(en, Language.En, Language.Ru));
    }

    [Fact]
    public void UnmappableCharKept() => Assert.Equal("№", Conv("№", Language.En, Language.Ru));

    [Fact]
    public void CapsLockRendersUppercase()
    {
        var pair = TestSupport.FixturePair();
        var strokes = TestSupport.StrokesTyping("ghbdtn", (TableKeyLayout)pair.Layout(Language.En))
            .Select(s => new KeyStroke(s.KeyCode, false, true)).ToList();
        Assert.Equal("ПРИВЕТ", Converter.Render(strokes, pair.Layout(Language.Ru)));
    }

    [Fact]
    public void StrokesForUnknownCharIsNull()
    {
        var pair = TestSupport.FixturePair();
        var strokes = Converter.Strokes("a№", pair.Layout(Language.En));
        Assert.Equal(2, strokes.Count);
        Assert.NotNull(strokes[0]);
        Assert.Null(strokes[1]);
    }
}
