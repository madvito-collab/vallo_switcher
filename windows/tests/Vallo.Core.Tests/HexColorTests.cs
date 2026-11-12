using Vallo.Core;
using Xunit;
using static Vallo.Core.EngineAction;

namespace Vallo.Core.Tests;

/// Те же примеры, что HexColorTests.swift.
public class HexColorTests
{
    [Theory]
    [InlineData("№аааааа", "#ffffff")]
    [InlineData("#аааааа", "#ffffff")]
    [InlineData("№1ф2и3с", "#1a2b3c")]
    [InlineData("№ФФФ", "#AAA")]
    [InlineData("№вуфвиууа", "#deadbeef")]
    [InlineData("№aaaaaa", "#aaaaaa")]      // свитчер уже переключил буквы, № остался
    public void FixesColorTypedInRussian(string typed, string expected) => Assert.Equal(expected, HexColor.Fix(typed, requireHash: true));

    [Theory]
    [InlineData("№123456")]   // номер документа
    [InlineData("#123456")]   // уже правильно
    [InlineData("#abcdef")]
    [InlineData("#ааааа")]    // 5 знаков
    [InlineData("#привет")]
    [InlineData("аааааа")]    // без решётки — только в Фигме
    public void LeavesNonColors(string typed) => Assert.Null(HexColor.Fix(typed, requireHash: true));

    [Fact]
    public void FigmaColorFieldWithoutHash()
    {
        Assert.Equal("ffffff", HexColor.Fix("аааааа", requireHash: false));
        Assert.Equal("aaa", HexColor.Fix("ффф", requireHash: false));
        Assert.Null(HexColor.Fix("123", requireHash: false));
        Assert.Null(HexColor.Fix("сова", requireHash: false));
    }
}

/// Цвет в движке: набор в русской раскладке («№» — это Shift+3) и пробел, знак или Enter.
public class HexInEngineTests
{
    [Theory]
    [InlineData("№аааааа ", "#ffffff ")]
    [InlineData("цвет №1ф2и3с, ", "цвет #1a2b3c, ")]
    [InlineData("номер №123456 ", "номер №123456 ")]   // номер — не трогаем
    public void ColorTypedInRussian(string typed, string expected) =>
        Assert.Equal(expected, AbbreviationTests.Typed(typed, Language.Ru));

    [Fact]
    public void ColorBeforeEnter()
    {
        var engine = new Engine(EngineSupport.Win, EngineSupport.RealJudge(), Language.Ru);
        foreach (var s in AbbreviationTests.Strokes("№аааааа", EngineSupport.WinRu)) engine.HandleKey(new KeyEvent(s, false));
        Assert.Equal(EngineSupport.Consume(new Delete(7), new Insert("#ffffff"), new PressKey(0x0D)),
            engine.HandleKey(new KeyEvent(new KeyStroke(0x0D), false)));
    }

    [Fact]
    public void OffWhenAutoSwitchOff()
    {
        var engine = new Engine(EngineSupport.Win, EngineSupport.RealJudge(), Language.Ru) { AutoSwitchEnabled = false };
        foreach (var s in AbbreviationTests.Strokes("№аааааа ", EngineSupport.WinRu))
            Assert.Equal(EngineOutput.PassThrough, engine.HandleKey(new KeyEvent(s, false)));
    }
}
