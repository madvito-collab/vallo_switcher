using Vallo.Core;
using Xunit;
using static Vallo.Core.EngineAction;

namespace Vallo.Core.Tests;

/// «США — международная» (ENG INTL): апостроф — мёртвая клавиша. Найдено при проверке:
/// «ээээх» в английской раскладке («''''[») двойной Shift перевёл в «''''х».
public class DeadKeyTests
{
    const ushort Apostrophe = 0xDE, LeftBracket = 0xDB, C = 0x43;
    static readonly KeyStroke Dead = new(Apostrophe);

    /// Как ответила бы система: «'» + «'» → «''», «'» + «[» → «'[», «'» + «c» → «ç».
    static TableKeyLayout UsIntl()
    {
        var us = EngineSupport.WinUs;
        var table = us.Table.Where(kv => kv.Key.KeyCode != Apostrophe).ToDictionary(kv => kv.Key, kv => kv.Value);
        var compose = new Dictionary<KeyStroke, string>
        {
            [Dead] = "''", [new KeyStroke(LeftBracket)] = "'[", [new KeyStroke(C)] = "ç",
        };
        return new TableKeyLayout(Language.En, table, new Dictionary<KeyStroke, DeadKeyInfo> { [Dead] = new("'", compose) });
    }

    static readonly LayoutPair Pair = new(UsIntl(), EngineSupport.WinRu);

    [Fact]
    public void RenderComposesDeadKeys()
    {
        var en = Pair.En;
        Assert.Equal("''''''[", Converter.Render(TestSupport.StrokesTyping("ээээээх", EngineSupport.WinRu), en));
        Assert.Equal("ç", Converter.Render(TestSupport.StrokesTyping("эс", EngineSupport.WinRu), en));
        // мёртвая последней — на экране её ещё нет
        Assert.Equal("", Converter.Render([Dead], en));
    }

    [Fact]
    public void DoubleShiftConvertsAllOfIt()
    {
        var engine = new Engine(Pair, EngineSupport.RealJudge(), Language.En);
        foreach (var s in TestSupport.StrokesTyping("ээээээх", EngineSupport.WinRu))
            engine.HandleKey(new KeyEvent(s, false));
        Assert.Equal([new Delete(7), new Insert("ээээээх"), new SwitchLayout(Language.Ru)], engine.HandleDoubleShift(null));
    }

    [Fact]
    public void AccentedLetterCountsAsOne()
    {
        // «эс» в ENG INTL — «ç»: стираем один символ, не два
        var engine = new Engine(Pair, EngineSupport.RealJudge(), Language.En);
        foreach (var s in TestSupport.StrokesTyping("эс", EngineSupport.WinRu))
            engine.HandleKey(new KeyEvent(s, false));
        Assert.Equal([new Delete(1), new Insert("эс"), new SwitchLayout(Language.Ru)], engine.HandleDoubleShift(null));
    }

    [Fact]
    public void RussianTypedConvertsToApostrophes()
    {
        // набрано по-русски «эээээх» → двойной Shift: «э» — клавиша мёртвого апострофа
        var engine = new Engine(Pair, EngineSupport.RealJudge(), Language.Ru);
        foreach (var s in TestSupport.StrokesTyping("эээээх", EngineSupport.WinRu))
            engine.HandleKey(new KeyEvent(s, false));
        Assert.Equal([new Delete(6), new Insert("'''''["), new SwitchLayout(Language.En)], engine.HandleDoubleShift(null));
    }

    [Fact]
    public void SelectionFromRussianGetsApostrophes()
    {
        // выделили «эээээх» и перевели (найдено при проверке: оставалось «эээээ[»)
        Assert.Equal("'''''[", Converter.Convert("эээээх", Pair.Ru, Pair.En));
    }

    [Fact]
    public void SelectionWithApostrophesConverts()
    {
        Assert.Equal("ээээх", Converter.Convert("''''[", Pair.En, Pair.Ru));
    }
}
