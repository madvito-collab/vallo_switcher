using Xunit;

namespace Vallo.Core.Tests;

/// Перенос `Tests/ValloCoreTests/CommaFilterTests.swift` — на тех же ответах модели (`Data/test/sage-outputs.tsv`).
public class CommaFilterTests
{
    static readonly Dictionary<string, string> SageOutputs = File.ReadAllLines(TestSupport.RepoData("test/sage-outputs.tsv"))
        .Select(l => l.Split('\t'))
        .Where(p => p.Length >= 2)
        .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.Last()[1]);

    static TypoCorrector Typos => Spelling.Typos;

    static string Merged(string original, bool capitalize = false) =>
        CommaFilter.Merge(original, SageOutputs[original], Typos, capitalize);

    static string M(string original, string model, bool capitalize = false) => CommaFilter.Merge(original, model, Typos, capitalize);

    [Fact]
    public void SlangAndJargonKept()
    {
        Assert.Equal("короче, я хз что делать", Merged("короче я хз что делать"));
        Assert.Equal("я щас приду, ща напишу", Merged("я щас приду ща напишу"));
        Assert.Equal("спс за макет завтра созвонимся", Merged("спс за макет завтра созвонимся"));
        Assert.Equal("задеплоил на прод вроде всё ок", Merged("задеплоил на прод вроде всё ок"));
        Assert.Equal("пришли, пожалуйста, скрин ошибки", Merged("пришли пожалуйста скрин ошибки"));
    }

    [Fact]
    public void CommasAccepted()
    {
        Assert.Equal("я думаю, что это хорошая идея", Merged("я думаю что это хорошая идея"));
        // «ё» от модели не берём — её ставит наш словарь (свой переключатель, двойные не трогаем)
        Assert.Equal("когда ты придешь, мы пойдем гулять", Merged("когда ты придешь мы пойдем гулять"));
        Assert.Equal("он сказал, что не сможет прийти, потому что заболел", Merged("он сказал что не сможет прийти потому что заболел"));
    }

    [Fact]
    public void ModelYoIgnored()
    {
        // модель не ставит «ё» в двойных и не откатывает нашу
        Assert.Equal("все пришли", M("все пришли", "Всё пришли."));
        Assert.Equal("ещё не время", M("ещё не время", "еще не время"));
    }

    [Fact]
    public void SafeReplacements()
    {
        Assert.Equal("какой-то странный баг в приложении", Merged("какой то странный баг в приложении"));
        Assert.Equal("кто-то забыл закрыть дверь", Merged("ктото забыл закрыть дверь"));
        Assert.Equal("вобщем, всё нормально", Merged("вобщем всё нормально"));
    }

    [Fact]
    public void NoDotBetweenDigits() => Assert.Equal("у нас митинг в 15 00", Merged("у нас митинг в 15 00"));

    [Fact]
    public void CapitalizeOption()
    {
        Assert.Equal("спасибо большое, ты лучший", Merged("спасибо большое ты лучший"));
        Assert.Equal("Спасибо большое, ты лучший.", Merged("спасибо большое ты лучший", capitalize: true));
    }

    [Fact]
    public void NoCommaBeforeOwnDash()
    {
        // тире уже поставил человек — запятая модели перед ним лишняя: «по резюме, — не покажет»
        Assert.Equal("проверит требования по резюме — не покажет их",
            M("проверит требования по резюме — не покажет их", "проверит требования по резюме, — не покажет их"));
        Assert.Equal("Москва - столица", M("Москва - столица", "Москва, - столица"));
        Assert.Equal("раз -- два", M("раз -- два", "раз, -- два"));
        // своя запятая перед тире остаётся
        Assert.Equal("если придёт, — скажи", M("если придёт, — скажи", "если придёт, — скажи"));
        // а в других местах запятые модели по-прежнему берём
        Assert.Equal("я знаю, что он придёт — и всё", M("я знаю что он придёт — и всё", "я знаю, что он придёт — и всё"));
    }

    [Fact]
    public void OwnPunctuationKept()
    {
        Assert.Equal("Я дома.", M("Я дома.", "Я дома."));
        Assert.Equal("Он сказал «привет» и ушёл", M("Он сказал «привет» и ушёл", "Он сказал «привет» и ушёл."));
        Assert.Equal("см. (пример) тут", M("см. (пример) тут", "См. (пример) тут."));
        Assert.Equal("Что-то не так...", M("Что-то не так...", "Что-то не так..."));
        Assert.Equal("Привет. Как дела?", M("Привет. Как дела?", "Привет. Как дела?"));
        // запятая модели — после закрывающей кавычки
        Assert.Equal("он сказал «да», и ушёл", M("он сказал «да» и ушёл", "Он сказал «да», и ушёл."));
    }

    [Fact]
    public void WhitespaceKept() =>
        Assert.Equal("  я думаю,  что\tэто идея  ", M("  я думаю  что\tэто идея  ", "Я думаю, что это идея."));

    [Fact]
    public void NoCommaDotAtEnd() =>
        Assert.Equal("Привет, как дела.", M("привет как дела", "привет, как дела, ну", capitalize: true));

    [Fact]
    public void ModelAddsWordsRejected() =>
        // модель дописала слово — не принимаем
        Assert.Equal("ну давай", M("ну давай", "Ну давай, пожалуйста."));
}
