using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class JudgeTests
{
    static bool IsSwitch(Verdict v) => v is Verdict.SwitchLanguage;

    static readonly Judge smallJudge = new(
        new Lexicon(new[] { "hello", "help", "the" }),
        new Lexicon(new[] { "привет", "при", "будет" }),
        new HashSet<string>());

    [Fact]
    public void WaitsBeforeThirdLetter() =>
        Assert.Equal(Verdict.Wait, smallJudge.MidWord("gh", "пр", Language.En));

    [Fact]
    public void SwitchesOnThirdLetter() =>
        Assert.True(IsSwitch(smallJudge.MidWord("ghb", "при", Language.En)));

    [Fact]
    public void WaitsWhileValidInCurrent() =>
        Assert.Equal(Verdict.Wait, smallJudge.MidWord("hel", "рук", Language.En));

    [Fact]
    public void CommaIsCutInEnglish()
    {
        // в EN запятая отрезается — остаётся 2 буквы
        Assert.Equal(Verdict.Wait, smallJudge.MidWord(",el", "буд", Language.En));
        Assert.True(IsSwitch(smallJudge.MidWord(",elt", "буде", Language.En)));
    }

    [Fact]
    public void WordEndDecisions()
    {
        Assert.True(IsSwitch(smallJudge.WordEnd("ghbdtn", "привет", Language.En)));
        Assert.Equal(Verdict.Keep, smallJudge.WordEnd("hello", "руддщ", Language.En));
    }

    [Fact]
    public void ShortWordsFromList()
    {
        // 1–2 буквы исправляем только по списку частых слов, и только на конце слова
        Assert.True(IsSwitch(smallJudge.WordEnd("jr", "ок", Language.En)));
        Assert.True(IsSwitch(smallJudge.WordEnd("z", "я", Language.En)));
        Assert.True(IsSwitch(smallJudge.WordEnd("щл", "ok", Language.Ru)));
        Assert.True(IsSwitch(smallJudge.WordEnd("Ш", "I", Language.Ru)));
        // настоящее короткое слово текущего языка не трогаем
        Assert.Equal(Verdict.Keep, smallJudge.WordEnd("ok", "щл", Language.En));
        Assert.Equal(Verdict.Keep, smallJudge.WordEnd("on", "щт", Language.En));
        // нет в списке — не трогаем
        Assert.Equal(Verdict.Keep, smallJudge.WordEnd("xx", "чч", Language.En));
        // посреди слова короткие не решаем
        Assert.Equal(Verdict.Wait, smallJudge.MidWord("jr", "ок", Language.En));
    }

    [Fact]
    public void SlangIsKept()
    {
        // разговорные коротыши не трогаем: «ща» — не ошибка раскладки для «of»
        Assert.Equal(Verdict.Keep, smallJudge.WordEnd("ща", "of", Language.Ru));
        Assert.Equal(Verdict.Keep, smallJudge.WordEnd("чо", "xj", Language.Ru));
        Assert.Equal(Verdict.Keep, smallJudge.WordEnd("спс", "cgc", Language.Ru));
        Assert.Equal(Verdict.Wait, smallJudge.MidWord("пжлст", "g;kcn", Language.Ru));
    }

    [Fact]
    public void LearnedWordBlocksSwitch()
    {
        var judge = new Judge(
            new Lexicon(new[] { "hello", "help", "the" }),
            new Lexicon(new[] { "привет", "при", "будет" }),
            new HashSet<string> { "ghbdtn" });
        Assert.Equal(Verdict.Wait, judge.MidWord("ghb", "при", Language.En));
        Assert.Equal(Verdict.Keep, judge.WordEnd("ghbdtn", "привет", Language.En));
    }

    [Fact]
    public void SpecialTextCases()
    {
        Assert.True(SpecialText.IsSpecial("me@mail"));
        Assert.True(SpecialText.IsSpecial("site.com"));
        Assert.True(SpecialText.IsSpecial("+79991234567"));
        Assert.True(SpecialText.IsSpecial("https://x"));
        Assert.False(SpecialText.IsSpecial("k.lb"));   // это «люди»
        Assert.Equal(Verdict.Wait, smallJudge.MidWord("abc1", "фис1", Language.En));
    }

    [Fact]
    public void RevertsWhenConvertedStopsBeingPrefix()
    {
        // словари сопоставимого размера: на словаре из одного слова сглаживание само завышает оценку
        var judge = new Judge(new Lexicon(new[] { "aghbz", "ghbza" }),
            new Lexicon(new[] { "при", "дом", "кот", "лес", "сад" }), new HashSet<string>());
        Assert.True(judge.ShouldRevert("ghbz", "прия", Language.En));
        var judge2 = new Judge(new Lexicon(new[] { "ghost", "abz" }), new Lexicon(new[] { "привет" }), new HashSet<string>());
        Assert.False(judge2.ShouldRevert("ghbd", "прив", Language.En));
    }

    [Fact]
    public void RealDictionaries()
    {
        var judge = new Judge(TestSupport.RealEN, TestSupport.RealRU, new HashSet<string>());
        Assert.True(IsSwitch(judge.MidWord("ghb", "при", Language.En)));
        Assert.Equal(Verdict.Wait, judge.MidWord("hel", "руд", Language.En));
        // «руд» — нормальное русское начало (руда), поэтому «hello» в RU-раскладке ловится на 4-й букве
        Assert.Equal(Verdict.Wait, judge.MidWord("руд", "hel", Language.Ru));
        Assert.True(IsSwitch(judge.MidWord("рудд", "hell", Language.Ru)));
        Assert.Equal(Verdict.Keep, judge.WordEnd("world", "цщкдв", Language.En));
    }

    [Fact]
    public void CoreAndAbbreviationHelpers()
    {
        Assert.Equal("ёЁ", Judge.Core("1ёЁ,"));
        Assert.True(Judge.IsAbbreviation("ЁЖ"));
        Assert.False(Judge.IsAbbreviation("Ёж"));
    }
}
