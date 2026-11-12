using Vallo.Core;
using Xunit;
using static Vallo.Core.EngineAction;

namespace Vallo.Core.Tests;

static class EngineSupport
{
    public static readonly LayoutPair Win = TestSupport.FixturePair();
    public static TableKeyLayout WinUs => (TableKeyLayout)Win.En;
    public static TableKeyLayout WinRu => (TableKeyLayout)Win.Ru;

    // судья — класс, и выученные слова движок пишет прямо в него: каждому движку свой
    public static Judge SmallJudge() => new(
        new Lexicon(new[] { "hello", "help", "the" }),
        new Lexicon(new[] { "привет", "при", "будет" }),
        new HashSet<string>());

    public static Judge RealJudge() => new(TestSupport.RealEN, TestSupport.RealRU, new HashSet<string>());

    public static Engine MakeEngine(bool auto = true) =>
        new(Win, SmallJudge(), Language.En) { AutoSwitchEnabled = auto };

    /// Нажимает клавиши, которые в EN-раскладке дают этот текст. Ответ на каждое нажатие.
    public static List<EngineOutput> Type(string text, Engine engine) =>
        TestSupport.StrokesTyping(text, WinUs).Select(s => engine.HandleKey(new KeyEvent(s, false))).ToList();

    public static readonly KeyEvent Backspace = new(new KeyStroke(0x08), false);

    public static EngineOutput Consume(params EngineAction[] actions) => new EngineOutput.Consume(actions);

    public static bool AllPass(IEnumerable<EngineOutput> outs) => outs.All(o => o == EngineOutput.PassThrough);
}

public class EngineTests
{
    static Engine MakeEngine(bool auto = true) => EngineSupport.MakeEngine(auto);
    static List<EngineOutput> Type(string text, Engine e) => EngineSupport.Type(text, e);
    static EngineOutput Consume(params EngineAction[] a) => EngineSupport.Consume(a);
    static readonly EngineOutput Pass = EngineOutput.PassThrough;

    [Fact]
    public void AutoSwitchMidWord()
    {
        var engine = MakeEngine();
        var outs = Type("ghbdtn", engine);
        Assert.Equal(Pass, outs[0]);
        Assert.Equal(Pass, outs[1]);
        Assert.Equal(Consume(new Delete(2), new Insert("при"), new SwitchLayout(Language.Ru)), outs[2]);
        Assert.True(EngineSupport.AllPass(outs.Skip(3)));
        Assert.Equal(Language.Ru, engine.CurrentLanguage);
        Assert.NotNull(engine.LastDecision);
    }

    [Fact]
    public void UndoRightAfterAutoSwitch()
    {
        var engine = MakeEngine();
        Type("ghbdtn ", engine);
        // первый откат — только вернуть слово, не запоминать (вдруг Shift нажат случайно)
        Assert.Equal(new EngineAction[] { new Delete(7), new Insert("ghbdtn "), new SwitchLayout(Language.En) },
            engine.HandleDoubleShift(null));
        Assert.Equal(Language.En, engine.CurrentLanguage);
    }

    [Fact]
    public void SecondUndoOfSameWordLearns()
    {
        var engine = MakeEngine();
        Type("ghbdtn ", engine);
        engine.HandleDoubleShift(null);
        // не запомнено — второй раз снова исправляется
        var again = Type("ghbdtn ", engine);
        Assert.Equal(Consume(new Delete(2), new Insert("при"), new SwitchLayout(Language.Ru)), again[2]);
        Assert.Equal(new EngineAction[] { new Delete(7), new Insert("ghbdtn "), new SwitchLayout(Language.En), new Learn("ghbdtn") },
            engine.HandleDoubleShift(null));
        // запомнено — третий раз не трогаем
        Assert.True(EngineSupport.AllPass(Type("ghbdtn", engine)));
    }

    [Fact]
    public void UndoOfDifferentWordDoesNotLearn()
    {
        var engine = MakeEngine();
        Type("ghbdtn ", engine);
        engine.HandleDoubleShift(null);
        Type("ghbd ", engine);
        Assert.Equal(new EngineAction[] { new Delete(5), new Insert("ghbd "), new SwitchLayout(Language.En) },
            engine.HandleDoubleShift(null));
    }

    [Fact]
    public void UndoWhileStillTypingWord()
    {
        var engine = MakeEngine();
        Type("ghbd", engine);
        Assert.Equal(new EngineAction[] { new Delete(4), new Insert("ghbd"), new SwitchLayout(Language.En) },
            engine.HandleDoubleShift(null));
    }

    [Fact]
    public void UndoExpiresOnNextWord()
    {
        var engine = MakeEngine();
        Type("ghbdtn x", engine);
        // отката уже нет — сценарий В: всё набранное в другую раскладку
        Assert.Equal(new EngineAction[] { new Delete(8), new Insert("ghbdtn x"), new SwitchLayout(Language.En) },
            engine.HandleDoubleShift(null));
    }

    [Fact]
    public void DoubleShiftConvertsWholeRun()
    {
        var engine = MakeEngine(auto: false);
        Type("ghbdtn rfr ltkf", engine);
        Assert.Equal(new EngineAction[] { new Delete(15), new Insert("привет как дела"), new SwitchLayout(Language.Ru) },
            engine.HandleDoubleShift(null));
        Assert.Equal(Language.Ru, engine.CurrentLanguage);
    }

    [Fact]
    public void DoubleShiftSelection()
    {
        var engine = MakeEngine();
        Assert.Equal(new EngineAction[] { new ReplaceSelection("hello"), new SwitchLayout(Language.En) },
            engine.HandleDoubleShift("руддщ"));
        Assert.Equal(new EngineAction[] { new ReplaceSelection("привет"), new SwitchLayout(Language.Ru) },
            engine.HandleDoubleShift("ghbdtn"));
    }

    [Fact]
    public void DoubleShiftSelectionWithNothingToConvertDoesNothing()
    {
        // Excel: Ctrl+C вслепую скопировал ячейку с числом — вставлять обратно то же самое незачем
        var engine = MakeEngine();
        Assert.Empty(engine.HandleDoubleShift("123"));
        Assert.Empty(engine.HandleDoubleShift("1\t25\r\n"));
        Assert.Equal(Language.En, engine.CurrentLanguage);
    }

    [Fact]
    public void DoubleShiftWithNothingDoesNothing()
    {
        var engine = MakeEngine();
        Assert.Empty(engine.HandleDoubleShift(null));
        Assert.Empty(engine.HandleDoubleShift(""));
        Assert.Equal(Language.En, engine.CurrentLanguage);
    }

    [Fact]
    public void CommandComboResetsBuffer()
    {
        var engine = MakeEngine(auto: false);
        Type("ghbd", engine);
        var ctrlZ = new KeyEvent(new KeyStroke(0x5A), true);
        Assert.Equal(Pass, engine.HandleKey(ctrlZ));
        Assert.Empty(engine.HandleDoubleShift(null));
    }

    [Fact]
    public void NavigationResetsBuffer()
    {
        var engine = MakeEngine(auto: false);
        Type("ghbd", engine);
        Assert.Equal(Pass, engine.HandleKey(new KeyEvent(new KeyStroke(0x25), false)));
        Assert.Empty(engine.HandleDoubleShift(null));
    }

    [Fact]
    public void CommaInsideWordIsLetter()
    {
        var engine = MakeEngine(auto: false);
        Type(",eltn", engine);
        Assert.Equal(new EngineAction[] { new Delete(5), new Insert("будет"), new SwitchLayout(Language.Ru) },
            engine.HandleDoubleShift(null));
    }

    [Fact]
    public void BackspaceShrinksBuffer()
    {
        var engine = MakeEngine(auto: false);
        Type("ghbdtnn", engine);
        Assert.Equal(Pass, engine.HandleKey(EngineSupport.Backspace));
        Assert.Equal(new EngineAction[] { new Delete(6), new Insert("привет"), new SwitchLayout(Language.Ru) },
            engine.HandleDoubleShift(null));
    }

    [Fact]
    public void ShortWordsOnlyFromList()
    {
        var engine = MakeEngine();
        Assert.True(EngineSupport.AllPass(Type("xx ", engine)));
        var outs = Type("jr ", MakeEngine());
        Assert.Equal(Consume(new Delete(2), new Insert("ок "), new SwitchLayout(Language.Ru)), outs[2]);
    }

    [Fact]
    public void AutoSwitchDisabled()
    {
        var engine = MakeEngine(auto: false);
        Assert.True(EngineSupport.AllPass(Type("ghbdtn ", engine)));
    }

    [Fact]
    public void WordEndSwitch()
    {
        // начало «xyzp» в EN-словаре есть (xyzpq), поэтому посреди слова ждём; на пробеле — слово RU
        var judge = new Judge(new Lexicon(new[] { "xyzpq" }), new Lexicon(new[] { "чняз" }), new HashSet<string>());
        var engine = new Engine(EngineSupport.Win, judge, Language.En);
        var outs = Type("xyzp ", engine);
        Assert.True(EngineSupport.AllPass(outs.Take(4)));
        Assert.Equal(Consume(new Delete(4), new Insert("чняз "), new SwitchLayout(Language.Ru)), outs[4]);
    }

    [Fact]
    public void HasTypedTextTracksBuffer()
    {
        // есть набранное — значит, свежего выделения быть не может (клик и стрелки сбрасывают буфер)
        var engine = MakeEngine(auto: false);
        Assert.False(engine.HasTypedText);
        Type("ghb", engine);
        Assert.True(engine.HasTypedText);
        engine.Reset();
        Assert.False(engine.HasTypedText);
    }

    [Fact]
    public void WrongShortWordRevertedByNextWord()
    {
        // «щт» (в RU) → «on»; следом «привет» пришлось вернуть в RU — значит, и «on» было ошибкой
        var engine = new Engine(EngineSupport.Win, EngineSupport.SmallJudge(), Language.Ru);
        var shortOut = Type("on ", engine);
        Assert.Equal(Consume(new Delete(2), new Insert("on "), new SwitchLayout(Language.En)), shortOut[2]);
        var next = Type("ghb", engine);
        Assert.Equal(Consume(new Delete(5), new Insert("щт при"), new SwitchLayout(Language.Ru)), next[2]);
        Assert.Equal(Language.Ru, engine.CurrentLanguage);
    }

    [Fact]
    public void KeptShortWordFollowsNextWord()
    {
        // «ща» набрано в EN-раскладке → «of» (настоящее слово, оставлено); следом русское — значит, и «of» было «ща»
        var engine = MakeEngine();
        Assert.True(EngineSupport.AllPass(Type("of ", engine)));
        var next = Type("ghb", engine);
        Assert.Equal(Consume(new Delete(5), new Insert("ща при"), new SwitchLayout(Language.Ru)), next[2]);
    }

    [Fact]
    public void ShortWordKeptWhenNextWordAgrees()
    {
        // «jr» → «ок», дальше русское слово — это подтверждение, ничего не возвращаем
        var engine = MakeEngine();
        Type("jr ", engine);
        Assert.True(EngineSupport.AllPass(Type("ghbdtn", engine)));
    }

    [Fact]
    public void CanUndoOnlyRightAfterAutoSwitch()
    {
        var engine = MakeEngine();
        Assert.False(engine.CanUndo);
        Type("ghbdtn ", engine);
        Assert.True(engine.CanUndo);
        Type("x", engine);
        Assert.False(engine.CanUndo);
    }

    [Fact]
    public void DuplicateLayoutNotificationKeepsUndo()
    {
        // уведомление о смене раскладки на ту же, что стоит, — эхо нашего переключения, откат не стирает
        var engine = MakeEngine();
        Type("ghb", engine);
        engine.ExternalLayoutChange(Language.Ru);
        Type("dtn ", engine);
        Assert.Equal(new EngineAction[] { new Delete(7), new Insert("ghbdtn "), new SwitchLayout(Language.En) },
            engine.HandleDoubleShift(null));
    }

    [Fact]
    public void ReturnChecksWordEnd()
    {
        var judge = new Judge(new Lexicon(new[] { "xyzpq" }), new Lexicon(new[] { "чняз" }), new HashSet<string>());
        var engine = new Engine(EngineSupport.Win, judge, Language.En);
        Type("xyzp", engine);
        var enter = new KeyEvent(new KeyStroke(0x0D), false);
        Assert.Equal(Consume(new Delete(4), new Insert("чняз"), new SwitchLayout(Language.Ru), new PressKey(0x0D)),
            engine.HandleKey(enter));
        Assert.False(engine.HasTypedText);
    }

    [Fact]
    public void ReturnWithoutSwitchPassesThrough()
    {
        var engine = MakeEngine(auto: false);
        Type("ghbd", engine);
        var tab = new KeyEvent(new KeyStroke(0x09), false);
        Assert.Equal(Pass, engine.HandleKey(tab));
        Assert.False(engine.HasTypedText);
    }

    [Fact]
    public void UpdateLayoutsResetsBuffer()
    {
        // на Маке здесь ISO-пара; у Windows одна пара раскладок — подставляем её заново
        var engine = MakeEngine(auto: false);
        Type("ghbd", engine);
        engine.UpdateLayouts(TestSupport.FixturePair());
        Assert.False(engine.HasTypedText);
    }

    [Fact]
    public void BufferConversionUpdatesDecision()
    {
        var engine = MakeEngine(auto: false);
        Type("ghbdtn", engine);
        engine.HandleDoubleShift(null);
        Assert.Contains("всё набранное", engine.LastDecision);
    }

    [Fact]
    public void ExternalLayoutChangeResets()
    {
        var engine = MakeEngine(auto: false);
        Type("ghbd", engine);
        engine.ExternalLayoutChange(Language.Ru);
        Assert.Equal(Language.Ru, engine.CurrentLanguage);
        Assert.Empty(engine.HandleDoubleShift(null));
    }

    [Fact]
    public void ThirdLayoutKeepsSilent()
    {
        // третья раскладка (не RU и не EN) — молчим, пока не вернут RU/EN
        var engine = MakeEngine();
        engine.ExternalLayoutChange(null);
        Assert.True(EngineSupport.AllPass(Type("ghbdtn ", engine)));
        engine.ExternalLayoutChange(Language.En);
        var outs = Type("ghbdtn", engine);
        Assert.Equal(Consume(new Delete(2), new Insert("при"), new SwitchLayout(Language.Ru)), outs[2]);
    }
}

public class AbbreviationTests
{
    /// Нажатия для текста с заглавными: заглавная — та же клавиша с Shift.
    public static List<KeyStroke> Strokes(string text, TableKeyLayout layout) =>
        text.Select(ch =>
        {
            var s = TestSupport.StrokesTyping(char.ToLowerInvariant(ch).ToString(), layout)[0];
            return char.IsUpper(ch) ? new KeyStroke(s.KeyCode, true, false) : s;
        }).ToList();

    static bool Consumed(string text, Language language)
    {
        var engine = new Engine(EngineSupport.Win, EngineSupport.RealJudge(), language);
        var layout = language == Language.Ru ? EngineSupport.WinRu : EngineSupport.WinUs;
        return Strokes(text, layout).Any(s => engine.HandleKey(new KeyEvent(s, false)) != EngineOutput.PassThrough);
    }

    [Fact]
    public void CapsAbbreviationsStayInTheirLayout()
    {
        // аббревиатуры заглавными не переводим
        foreach (var w in new[] { "ВГСЧ ", "ЖКХ ", "СПГ ", "ЧС ", "МЧС ", "СНИЛС " }) Assert.False(Consumed(w, Language.Ru), w);
        foreach (var w in new[] { "PDF ", "API ", "KPI ", "SDK " }) Assert.False(Consumed(w, Language.En), w);
    }

    [Fact]
    public void OrdinaryWordsStillSwitch()
    {
        Assert.True(Consumed("ghbdtn ", Language.En));
        Assert.True(Consumed("Ghbdtn ", Language.En));   // заглавная только первая — это не аббревиатура
    }

    /// Что окажется на экране после набора.
    public static string Typed(string text, Language language)
    {
        var engine = new Engine(EngineSupport.Win, EngineSupport.RealJudge(), language);
        var screen = "";
        // клавиши — по буквам текста (что человек имел в виду), а раскладка на экране — `language`
        var cyrillic = text.Any(c => Lexicon.IsInAlphabet(char.ToLowerInvariant(c), Language.Ru));
        var layout = cyrillic ? EngineSupport.WinRu : EngineSupport.WinUs;
        foreach (var stroke in Strokes(text, layout))
        {
            var shownIn = engine.CurrentLanguage;
            switch (engine.HandleKey(new KeyEvent(stroke, false)))
            {
                case EngineOutput.Consume c:
                    foreach (var a in c.Actions)
                    {
                        if (a is Delete d) screen = screen[..^d.N];
                        if (a is Insert i) screen += i.Text;
                    }
                    break;
                default:
                    screen += EngineSupport.Win.Layout(shownIn).Character(stroke) ?? "";
                    break;
            }
        }
        return screen;
    }

    [Fact]
    public void KnownAbbreviationsFromOtherLayout()
    {
        // набрано в чужой раскладке: маленькими — маленькими, хоть одна заглавная — все заглавные
        Assert.Equal("pdf ", Typed("pdf ", Language.Ru));   // на экране было бы «зва»
        Assert.Equal("PDF ", Typed("Pdf ", Language.Ru));
        Assert.Equal("PDF ", Typed("PDF ", Language.Ru));
        Assert.Equal("жкх ", Typed("жкх ", Language.En));
        Assert.Equal("ЖКХ ", Typed("Жкх ", Language.En));
        Assert.Equal("ui ", Typed("ui ", Language.Ru));
    }

    [Fact]
    public void KnownAbbreviationCaseInOwnLayout()
    {
        Assert.Equal("PDF ", Typed("Pdf ", Language.En));
        Assert.Equal("ЖКХ ", Typed("Жкх ", Language.Ru));
        Assert.Equal("pdf ", Typed("pdf ", Language.En));
        Assert.Equal("Привет ", Typed("Привет ", Language.Ru));
    }

    [Fact]
    public void AbbreviationsDontLookLikeOtherWords()
    {
        // аббревиатура, набранная в чужой раскладке, не должна совпадать с настоящим словом того языка
        var pair = EngineSupport.Win;
        foreach (var (language, list) in SwitcherRules.Shared.Abbreviations)
        {
            var own = language == Language.Ru ? EngineSupport.WinRu : EngineSupport.WinUs;
            var other = language.Other();
            var clashes = list.Where(abbr =>
            {
                var shown = Converter.Render(TestSupport.StrokesTyping(abbr, own), pair.Layout(other));
                return (other == Language.Ru ? TestSupport.RealRU : TestSupport.RealEN).IsWord(shown);
            }).OrderBy(s => s, StringComparer.Ordinal).ToList();
            Assert.True(clashes.Count == 0, $"совпали с настоящими словами: {string.Join(", ", clashes)}");
        }
    }

    [Fact]
    public void JudgeKnowsAbbreviation()
    {
        Assert.True(Judge.IsAbbreviation("ВГСЧ"));
        Assert.True(Judge.IsAbbreviation("PDF,"));
        Assert.False(Judge.IsAbbreviation("Привет"));
        Assert.False(Judge.IsAbbreviation("Я"));      // одна буква — не аббревиатура
    }
}

public class UnitsAfterNumberTests
{
    [Fact]
    public void UnitsAfterNumberStayRussian()
    {
        // «руб» — это «he,» в английской раскладке, «шт» — «in», «сек» — «ctr»: после числа это единицы
        foreach (var s in new[] { "850руб ", "850 руб ", "100 руб. ", "5 шт ", "5шт ", "30 сек ", "2 млн руб " })
            Assert.Equal(s, AbbreviationTests.Typed(s, Language.Ru));
    }

    [Fact]
    public void UnitGluedToNumberSwitches()
    {
        // «850руб» в английской раскладке — «850he,»: «he» — английское слово, «б» — запятая. Слитно с числом — это рубли
        foreach (var s in new[] { "850руб ", "20руб, ", "5мин ", "2млн " })
            Assert.Equal(s, AbbreviationTests.Typed(s, Language.En));
    }

    [Fact]
    public void UnitWithSpaceOrShortStaysEnglish()
    {
        // с пробелом это может быть английский текст, а «24h» — «24 часа»
        foreach (var s in new[] { "at 20 he, said ", "24h ", "5in " })
            Assert.Equal(s, AbbreviationTests.Typed(s, Language.En));
    }

    [Fact]
    public void WithoutNumberStillSwitches()
    {
        // «in», набранное в русской раскладке, по-прежнему исправляется
        Assert.Equal("in ", AbbreviationTests.Typed("шт ", Language.Ru));
        Assert.Equal("I live in ", AbbreviationTests.Typed("I live in ", Language.Ru));
    }
}

public class CommaLetterTests
{
    [Fact]
    public void WordsStartingWithBInWrongLayout()
    {
        // «б» в английской раскладке — запятая: «был» = «,sk», свитчер считал его словом из двух букв и не трогал
        foreach (var s in new[] { "был ", "бог ", "бок ", "бар ", "были " })
            Assert.Equal(s, AbbreviationTests.Typed(s, Language.En));
        Assert.Equal("I was ok ", AbbreviationTests.Typed("I was ok ", Language.En));
    }
}
