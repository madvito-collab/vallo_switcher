using Xunit;

namespace Vallo.Core.Tests;

/// Опечатки и «ё» на лету. Перенос раздела «Опечатки на лету» и «ё на лету» из `Tests/ValloCoreTests/LiveTypographerTests.swift`.
public class LiveTyposTests
{
    static readonly LiveOptions WithTypos = new(Typography: true, Typos: Spelling.Typos);
    /// Исправлятор, который знает слова из словаря «ё» (как в приложении) — отдельный, общий не трогаем.
    static readonly TypoCorrector TyposWithYo = MakeTyposWithYo();
    static readonly LiveOptions WithYo = new(Typography: true, Typos: TyposWithYo, Yo: Spelling.Yo);

    static TypoCorrector MakeTyposWithYo()
    {
        var t = TypoCorrector.Load(TestSupport.RepoData("wordlists/ru-typos.txt"), []);
        t.YoWords = Spelling.Yo;
        return t;
    }

    static List<TypoEdit?> TypeAll(LiveTypographer live, string text, LiveOptions options) =>
        TextElements.Split(text).Select(c => live.Typed(c, Language.Ru, options)).ToList();

    static LiveTypographer AtLineStart()
    {
        var live = new LiveTypographer();
        live.Reset(knownLineStart: true);
        return live;
    }

    [Fact]
    public void TypoOnSpace() =>
        // правка минимальная: общее начало «пр» не трогаем
        Assert.Equal(new TypoEdit(3, "ивет "), TypeAll(AtLineStart(), "првет ", WithTypos)[^1]);

    [Fact]
    public void TypoAndTypographyTogether() =>
        Assert.Equal(new TypoEdit(3, "ивет "), TypeAll(AtLineStart(), "в првет ", WithTypos)[^1]);

    [Fact]
    public void MisspellingOnSpaceAndUndo()
    {
        var live = AtLineStart();
        var edit = TypeAll(live, "вобщем ", WithTypos)[^1];
        Assert.NotNull(live.Backspace());
        Assert.Equal("вобщем", live.UndoneTypoWord);
        Assert.True(edit!.Value.Delete > 0);
    }

    [Fact]
    public void YoOnSpace() =>
        Assert.Equal(new TypoEdit(1, "ё "), TypeAll(AtLineStart(), "еще ", WithYo)[^1]);

    [Fact]
    public void YoWithTyposOff() =>
        // свой переключатель: опечатки и типографика выключены, а «ё» ставится
        Assert.Equal(new TypoEdit(1, "ё "), TypeAll(AtLineStart(), "еще ", new LiveOptions(Typography: false, Yo: Spelling.Yo))[^1]);

    [Fact]
    public void YoUndoDoesNotTeach()
    {
        // «е» вместо «ё» — не ошибка: отмена не кладёт слово в «Не исправлять слова»
        var live = AtLineStart();
        TypeAll(live, "еще ", WithYo);
        Assert.Equal(new TypoEdit(2, "е "), live.Backspace());
        Assert.Null(live.UndoneTypoWord);
    }

    [Fact]
    public void RealYoWordIsNotATypo() =>
        // «введем» нет в словаре опечаток — без «ё»-словаря исправлятор «чинил» его в «ведём»
        Assert.Equal(new TypoEdit(2, "ём "), TypeAll(AtLineStart(), "введем ", WithYo)[^1]);

    [Fact]
    public void TypoThenYo()
    {
        // опечатка, потом «ё»: «серьезон» → «серьезно» → «серьёзно»
        var live = AtLineStart();
        Assert.Equal(new TypoEdit(4, "ёзно "), TypeAll(live, "серьезон ", WithYo)[^1]);
        Assert.NotNull(live.Backspace());
        Assert.Equal("серьезон", live.UndoneTypoWord);
    }

    [Fact]
    public void TypoUndoTeachesWord()
    {
        var live = AtLineStart();
        TypeAll(live, "приввет ", WithTypos);
        Assert.Equal(new TypoEdit(3, "вет "), live.Backspace());
        Assert.Equal("приввет", live.UndoneTypoWord);
    }

    [Fact]
    public void FirstWordAfterClickIsCorrected() =>
        // после клика в поле (обычный сброс) первое слово — целое слово, его исправляем
        Assert.Equal(new TypoEdit(3, "ивет "), TypeAll(new LiveTypographer(), "првет ", WithTypos)[^1]);

    [Fact]
    public void TailAfterMidWordResetIsNotCorrected()
    {
        // свитчер исправил раскладку посреди слова — в памяти хвост, его не трогаем
        var live = new LiveTypographer();
        live.Reset(midWord: true);
        Assert.All(TypeAll(live, "ивет ", WithTypos), e => Assert.Null(e));
    }

    [Fact]
    public void TypoOffKeepsWord() =>
        Assert.All(TypeAll(AtLineStart(), "првет ", new LiveOptions(Typography: true)), e => Assert.Null(e));

    [Fact]
    public void TypoEditChangesLength()
    {
        var live = AtLineStart();
        TypeAll(live, "приввет ", WithTypos);
        Assert.True(live.LastEditChangedLength);
    }

    [Fact]
    public void TypographyOffTyposOn()
    {
        var edits = TypeAll(AtLineStart(), "раз - првет ", new LiveOptions(Typography: false, Typos: Spelling.Typos));
        Assert.Equal(new TypoEdit(3, "ивет "), edits[^1]);
        Assert.All(edits.SkipLast(1), e => Assert.Null(e));
    }

    [Fact]
    public void ParticleJoinedLive() =>
        Assert.Equal(new TypoEdit(3, "-то "), TypeAll(AtLineStart(), "какой то ", WithTypos)[^1]);

    [Fact]
    public void IgnoredWordStaysAfterUndo()
    {
        // как в приложении: отменили — слово в «Не исправлять слова», второй раз не трогаем
        var t = TypoCorrector.Load(TestSupport.RepoData("wordlists/ru-typos.txt"), []);
        var options = new LiveOptions(Typography: true, Typos: t);
        var live = AtLineStart();
        TypeAll(live, "приввет ", options);
        live.Backspace();
        t.Ignored = new HashSet<string>(t.Ignored) { live.UndoneTypoWord! };
        Assert.All(TypeAll(live, "приввет ", options), e => Assert.Null(e));
    }
}

/// Настройки опечаток и «ё» на странице и в файле.
public class TypoSettingsTests
{
    [Fact]
    public void OnByDefaultLikeMac()
    {
        var s = new AppSettings();
        Assert.True(s.TyposLive);
        Assert.True(s.YoLive);
        Assert.Empty(s.TypoIgnoredWords);
    }

    [Fact]
    public void PageShowsTyposButNotCommas()
    {
        var page = SettingsPage.State(new AppSettings { TypoIgnoredWords = ["приввет"] }, false, "0.4", null, e => e);
        var c = page["correct"]!;
        Assert.True((bool)c["typosAvailable"]!);
        Assert.False((bool)c["commasAvailable"]!);
        Assert.True((bool)c["typos"]!);
        Assert.True((bool)c["yo"]!);
        Assert.Equal("приввет", (string?)c["words"]![0]);
    }

    [Fact]
    public void TogglesAndWordList()
    {
        static System.Text.Json.JsonElement J(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement;
        var s = new AppSettings();
        s = SettingsPage.Apply(s, "typos", J("false"));
        s = SettingsPage.Apply(s, "yo", J("false"));
        Assert.False(s.TyposLive);
        Assert.False(s.YoLive);
        s = SettingsPage.AddWord(s, " Приввет ", "correct");
        Assert.Equal(["приввет"], s.TypoIgnoredWords);
        Assert.Empty(s.LearnedWords);
        s = SettingsPage.Apply(s, "words.correct", J("[]"));
        Assert.Empty(s.TypoIgnoredWords);
    }

    [Fact]
    public void IgnoredWordsSurviveFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "vallo-" + Guid.NewGuid().ToString("N") + ".json");
        new AppSettings { TyposLive = false, TypoIgnoredWords = ["приввет"] }.Save(path);
        var loaded = AppSettings.Load(path);
        Assert.False(loaded.TyposLive);
        Assert.Equal(["приввет"], loaded.TypoIgnoredWords);
        File.Delete(path);
    }
}

/// Двойной правый Shift по выделенному: опечатки → «ё» → типографика, как на Маке (запятые — позже).
public class SelectionCorrectorTests
{
    [Fact]
    public void TyposYoAndTypographyAtOnce() =>
        Assert.Equal("Привет, ещё «текст»\u00A0— тут какой-то",
            SelectionCorrector.Fix("Првет, еще \"текст\" - тут какой то", Spelling.App, Spelling.Yo, Language.Ru));

    [Fact]
    public void YoOffKeepsE() =>
        Assert.Equal("Привет, еще", SelectionCorrector.Fix("Првет, еще", Spelling.App, null, Language.Ru));

    [Fact]
    public void WithoutDictionariesOnlyTypography() =>
        Assert.Equal("раз\u00A0— два", SelectionCorrector.Fix("раз - два", null, null, Language.Ru));

    [Fact]
    public void CorrectTextUntouched()
    {
        // правильный текст с жаргоном: опечатки и «ё» его не трогают — остаётся только типографика (неразрывный пробел после «в»)
        const string text = "Привет! Как дела? Скинь макет в фигме, созвонимся завтра.";
        Assert.Equal(Typographer.Format(text, final: true, hint: Language.Ru),
                     SelectionCorrector.Fix(text, Spelling.App, Spelling.Yo, Language.Ru));
    }
}
