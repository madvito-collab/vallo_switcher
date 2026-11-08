using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class LiveTypographerTests
{
    static readonly LiveOptions All = new(Typography: true, Emoji: EmojiBrackets.Millennial);

    static List<TypoEdit?> TypeAll(LiveTypographer live, string text) =>
        TextElements.Split(text).Select(c => live.Typed(c, Language.Ru, All)).ToList();

    [Fact]
    public void EmojiUndoCountsWhatPersonSees()
    {
        var live = new LiveTypographer();
        live.Reset(knownLineStart: true);
        var edits = TypeAll(live, "ок)) ");
        Assert.Equal(new TypoEdit(2, "😄 "), edits[^1]);
        // смайлик — один символ: Backspace стирает 2 (смайлик и пробел) и возвращает скобки
        Assert.Equal(new TypoEdit(2, ")) "), live.Backspace());
    }

    [Fact]
    public void DashOnSpaceAndUndo()
    {
        var live = new LiveTypographer();
        live.Reset(knownLineStart: true);
        Assert.Equal(new TypoEdit(2, " — "), TypeAll(live, "раз - ")[^1]);
        Assert.Equal(new TypoEdit(3, " - "), live.Backspace());
        // отменённое больше не трогаем
        Assert.Null(live.Typed("д", Language.Ru, All));
    }

    [Fact]
    public void BackspaceWithoutEditIsPlain()
    {
        var live = new LiveTypographer();
        live.Reset(knownLineStart: true);
        TypeAll(live, "привет");
        Assert.Null(live.Backspace());
    }

    [Fact]
    public void TypographyOffKeepsDashButEmojiWorks()
    {
        var live = new LiveTypographer();
        live.Reset(knownLineStart: true);
        var opts = new LiveOptions(Typography: false, Emoji: EmojiBrackets.Zoomer);
        Assert.All(TextElements.Split("раз - "), c => Assert.Null(live.Typed(c, Language.Ru, opts)));
        live.Reset(knownLineStart: true);
        // ToList: Select(...).Last() над списком в .NET зовёт функцию только для последнего элемента
        var last = TextElements.Split("ору))) ").Select(c => live.Typed(c, Language.Ru, opts)).ToList()[^1];
        Assert.Equal(new TypoEdit(3, "💀 "), last);
    }
}
