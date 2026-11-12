using System.IO.Compression;
using Xunit;
using Xunit.Abstractions;

namespace Vallo.Core.Tests;

/// Словари опечаток и «ё» — один раз на все тесты, как в приложении.
static class Spelling
{
    public static readonly TypoCorrector Typos = TypoCorrector.Load(TestSupport.RepoData("wordlists/ru-typos.txt"), []);
    public static readonly Yoficator Yo = Yoficator.Load(TestSupport.RepoData("wordlists/ru-yo.txt"));
    public static readonly HashSet<string> Modern =
        WordForms.Load(TestSupport.RepoData("wordlists/ru-modern.txt")).Select(Lexicon.Normalize).ToHashSet();

    /// Все формы слов: в репозитории сжаты — распаковываем один раз во временную папку (в установщике — при сборке).
    public static readonly SortedWordFile Forms = LoadForms();

    static SortedWordFile LoadForms()
    {
        var out_ = Path.Combine(Path.GetTempPath(), "vallo-ru-forms.txt");
        var gz = TestSupport.RepoData("wordlists/ru-forms.txt.gz");
        if (!File.Exists(out_) || File.GetLastWriteTimeUtc(out_) < File.GetLastWriteTimeUtc(gz))
        {
            var tmp = out_ + "." + Guid.NewGuid().ToString("N");
            using (var src = File.OpenRead(gz))
            using (var unzip = new GZipStream(src, CompressionMode.Decompress))
            using (var dst = File.Create(tmp)) unzip.CopyTo(dst);
            File.Move(tmp, out_, overwrite: true);
        }
        return SortedWordFile.Load(out_);
    }

    /// Как в приложении: словарь опечаток + «ё» + современные слова + все формы.
    public static TypoCorrector Full(IEnumerable<string>? ignored = null) =>
        new FullCorrector(ignored ?? []).Value;

    sealed class FullCorrector(IEnumerable<string> ignored)
    {
        public TypoCorrector Value { get; } = Make(ignored);
        static TypoCorrector Make(IEnumerable<string> ignored)
        {
            var t = TypoCorrector.Load(TestSupport.RepoData("wordlists/ru-typos.txt"), ignored);
            t.YoWords = Yo;
            t.ModernWords = Modern;
            t.Forms = Forms;
            return t;
        }
    }

    public static readonly TypoCorrector App = Full();
}

/// Детерминированный генератор для синтетических промахов (тот же, что в маковских тестах).
sealed class SeededRandom(ulong state)
{
    ulong state = state;
    public ulong Next() => state = unchecked(state * 6364136223846793005UL + 1442695040888963407UL);
    public int Int(int lo, int hi) => lo + (int)((Next() >> 33) % (ulong)(hi - lo));
    public T Pick<T>(IReadOnlyList<T> list) => list[Int(0, list.Count)];
}

/// Перенос `Tests/ValloCoreTests/TypoCorrectorTests.swift`.
public class TypoCorrectorTests(ITestOutputHelper output)
{
    static TypoCorrector Typos => Spelling.Typos;

    [Fact]
    public void FixesCommonTypos()
    {
        foreach (var t in new[] { "првет", "приввет", "привте" }) Assert.Equal("привет", Typos.Correct(t));
        Assert.Equal("пожалуйста", Typos.Correct("пожалуста"));
        Assert.Equal("сегодня", Typos.Correct("сегодян"));
        Assert.Equal("спасибо", Typos.Correct("спасиббо"));
        Assert.Equal("нормально", Typos.Correct("нормалньо"));
    }

    [Fact]
    public void MissingSoftSignAtEnd()
    {
        Assert.Equal("можешь", Typos.Correct("можеш"));
        Assert.Equal("будешь", Typos.Correct("будеш"));
        Assert.Equal("хочешь", Typos.Correct("хочеш"));
        Assert.Equal("Делаешь", Typos.Correct("Делаеш"));
        Assert.Null(Typos.Correct("можш"));      // два промаха — не трогаем
        Assert.Null(Typos.Correct("малыш"));     // настоящее слово на «ш»
    }

    [Fact]
    public void WorkVocabulary()
    {
        // рабочие слова — из письменного словаря (Википедия)
        (string, string)[] work = [("навки", "навыки"), ("тарфи", "тариф"), ("проетк", "проект"), ("компаия", "компания"),
            ("сотрудиник", "сотрудник"), ("зарплта", "зарплата"), ("настрйоки", "настройки"), ("подключеия", "подключения")];
        var fixedCount = work.Count(p => Typos.Correct(p.Item1) == p.Item2);
        output.WriteLine($"Рабочие: {fixedCount}/{work.Length}");
        Assert.True(fixedCount >= 6);
    }

    [Fact]
    public void KeepsCase()
    {
        Assert.Equal("Привет", Typos.Correct("Првет"));
        Assert.Equal("ПРИВЕТ", Typos.Correct("ПРВЕТ"));
    }

    [Fact]
    public void KeepsSlangItAndRare()
    {
        const string control = """
            фигма фигму фигме мокап мокапы фреймы задеплоил пушнуть засинкаемся ревью айфон нетфликс скрин скриншот
            пулл реквест коммит закоммитил смержить мерж фича баг багфикс деплой прод дизайнер дизайнеры юзер юзеры
            ща щас чо спс хз норм кек лол имхо телега телеге вотсап
            гугл гуглить загуглил апрув заапрувил дедлайн дедлайны созвон созвонимся митап кофейка кстати короче
            ваще типа капец офигеть прикольно залипать залип тусить зашквар хайп кринж кринжово вайб вайбкодить
            смузи латте бариста кейс кейсы бриф брифинг онбординг лендинг лендинга прототип прототипы фигмы планку уговорю
            """;
        var spoiled = control.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => Typos.Correct(w) is not null).ToList();
        Assert.Empty(spoiled);
    }

    [Fact]
    public void NamesAndBrandsKept()
    {
        foreach (var w in new[] { "Сбербанк", "Виталий", "Фигма", "Яндекс", "Телеграм", "Озон", "Авито", "Пушкин", "Москва", "Шмелев" })
            Assert.Null(Typos.Correct(w));
    }

    [Fact]
    public void HyphenParticles() =>
        Assert.Equal("какой-то баг кто-то", Typos.CorrectPhrase("какой то баг ктото"));

    [Fact]
    public void PronounToNotJoinedLive()
    {
        // «что то решение» — «то» здесь местоимение; на лету «что/как/чем» + «то» не склеиваем
        Assert.Equal("я думаю что то решение верное", Typos.JoinParticles("я думаю что то решение верное", live: true));
        Assert.Equal("какой-то баг", Typos.JoinParticles("какой то баг", live: true));
    }

    [Fact]
    public void CommonMisspellings()
    {
        (string, string)[] cases = [("помоему", "по-моему"), ("помойму", "по-моему"), ("вобщем", "в общем"), ("вообщем", "в общем"),
            ("всмысле", "в смысле"), ("какбы", "как бы"), ("зделать", "сделать"), ("извените", "извините"),
            ("щитаю", "считаю"), ("незнаю", "не знаю"), ("ево", "его"), ("будующем", "будущем"),
            ("всеравно", "всё равно"), ("компютер", "компьютер")];
        foreach (var (typed, fixedWord) in cases) Assert.Equal(fixedWord, Typos.Correct(typed));
    }

    [Fact]
    public void MisspellingsKeepCase()
    {
        Assert.Equal("По-моему", Typos.Correct("Помоему"));
        Assert.Equal("В общем", Typos.Correct("Вобщем"));
        Assert.Equal("СДЕЛАТЬ", Typos.Correct("ЗДЕЛАТЬ"));
    }

    [Fact]
    public void MisspellingsRespectMyWords()
    {
        var mine = TypoCorrector.Load(TestSupport.RepoData("wordlists/ru-typos.txt"), ["ево"]);
        Assert.Null(mine.Correct("ево"));
    }

    [Fact]
    public void MisspellingsAreNotRealWords()
    {
        // в списке не должно быть настоящих частых слов — иначе испортим правильный текст.
        // Эти три — ошибки, которые так часты в субтитрах, что попали в словарь; список их всё равно исправляет.
        HashSet<string> frequentErrors = ["вобщем", "всмысле", "агенство"];
        var known = SwitcherRules.Shared.Misspellings.Keys.Where(t => !frequentErrors.Contains(t) && Typos.IsKnown(t)).ToList();
        Assert.Empty(known);
    }

    [Fact]
    public void MixedCaseKept()
    {
        Assert.Null(Typos.Correct("пРвет"));
        Assert.Null(Typos.Correct("ПРвет"));
    }

    [Fact]
    public void IgnoredWordsKept()
    {
        var t = TypoCorrector.Load(TestSupport.RepoData("wordlists/ru-typos.txt"), ["приввет"]);
        Assert.Null(t.Correct("приввет"));
    }

    [Fact]
    public void SyntheticTypoQuality()
    {
        var rng = new SeededRandom(7);
        const string alphabet = "абвгдежзийклмнопрстуфхцчшщъыьэюя";
        var pool = Typos.WordsRange(300, 6000).Where(w => w.Length >= 5).ToList();
        int total = 0, right = 0, wrong = 0;
        for (var k = 0; k < 150; k++)
        {
            var w = rng.Pick(pool);
            var i = rng.Int(1, w.Length - 1);
            var typo = rng.Int(0, 4) switch
            {
                0 => w.Insert(i, alphabet[rng.Int(0, alphabet.Length)].ToString()),
                1 => w.Remove(i, 1),
                2 => w[..i] + w[i + 1] + w[i] + w[(i + 2)..],
                _ => w.Insert(i, w[i].ToString()),
            };
            if (Typos.IsKnown(typo)) continue;
            total++;
            var got = Typos.Correct(typo);
            if (got == w) right++;
            else if (got is not null) wrong++;
        }
        output.WriteLine($"Промахи: {total}, верно {right}, неверно {wrong}");
        Assert.True(right >= 0.75 * total, $"верно {right} из {total}");
        Assert.True(wrong <= 0.02 * total, $"неверно {wrong} из {total}");
    }

    [Fact]
    public void RareWordsSafety()
    {
        var rng = new SeededRandom(7);
        var rare = Typos.WordsRange(TypoCorrector.CandidateLimit, TypoCorrector.CandidateLimit + 100_000).Where(w => w.Length >= 5).ToList();
        var spoiled = Enumerable.Range(0, 3000).Select(_ => rng.Pick(rare)).Where(w => Typos.Correct(w) is not null).ToList();
        output.WriteLine($"Редкие: испорчено {spoiled.Count} из 3000: {string.Join(", ", spoiled.Take(5))}");
        Assert.True(spoiled.Count <= 0.005 * 3000);
    }

    [Fact]
    public void FastEnough()
    {
        var app = Spelling.App;
        app.Correct("приввет");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 200; i++) app.Correct("приввет");
        var perWord = sw.Elapsed.TotalMilliseconds / 200;
        output.WriteLine($"Correct: {perWord:F2} мс на слово");
        Assert.True(perWord < 5);
    }
}

/// Перенос `Tests/ValloCoreTests/YoficatorTests.swift`.
public class YoficatorTests
{
    static Yoficator Yo => Spelling.Yo;

    [Theory]
    [InlineData("еще", "ещё")]
    [InlineData("ее", "её")]
    [InlineData("далекий", "далёкий")]
    [InlineData("Шмелев", "Шмелёв")]
    [InlineData("всерьез", "всерьёз")]
    public void UnambiguousWords(string typed, string fixedWord) => Assert.Equal(fixedWord, Yo.Fix(typed));

    [Theory]
    // двойные (все/всё, Королев/Королёв) не трогаем никогда; уже с «ё» — тоже
    [InlineData("все")]
    [InlineData("узнаем")]
    [InlineData("небо")]
    [InlineData("ещё")]
    [InlineData("привет")]
    [InlineData("Королев")]
    public void AmbiguousAndDoneUntouched(string w) => Assert.Null(Yo.Fix(w));

    [Fact]
    public void KeepsCase()
    {
        Assert.Equal("Ещё", Yo.Fix("Еще"));
        Assert.Equal("ЕЩЁ", Yo.Fix("ЕЩЕ"));
        Assert.Null(Yo.Fix("еЩе"));
    }

    [Fact]
    public void ProperNamesOnlyCapitalized()
    {
        Assert.Equal("Ёлкин", Yo.Fix("Елкин"));
        Assert.Null(Yo.Fix("елкин"));
    }

    [Fact]
    public void Phrase() =>
        // голосовой ввод вставляет текст целиком — правый Shift по выделенному
        Assert.Equal("Да, ещё все верно. Её нет", Yo.FixPhrase("Да, еще все верно. Ее нет"));
}

/// Исправлятор на настоящих опечатках из реальной переписки (Tests/Data/typos-real.tsv): сколько ловит и сколько портит.
/// Перенос `Tests/ValloCoreTests/RealTyposTests.swift` — пороги те же.
public class RealTyposTests(ITestOutputHelper output)
{
    [Fact]
    public void Measure()
    {
        var cases = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData", "typos-real.tsv"))
            .Where(l => !l.StartsWith('#'))
            .Select(l => l.Split('\t'))
            .Where(p => p.Length == 2)
            .Select(p => (Typed: p[0], Expected: p[1] == "=" ? null : p[1]))
            .ToList();
        var corrector = Spelling.App;
        int fixedCount = 0;
        List<string> missed = [], wrong = [], damaged = [];
        foreach (var c in cases)
        {
            var got = corrector.Correct(c.Typed) is { } g ? Lexicon.Normalize(g) : null;
            if (c.Expected is { } e)
            {
                if (got == Lexicon.Normalize(e)) fixedCount++;
                else if (got is not null) wrong.Add($"{c.Typed}→{got}");
                else missed.Add(c.Typed);
            }
            else if (got is not null) damaged.Add($"{c.Typed}→{got}");
        }
        var typosTotal = cases.Count(c => c.Expected is not null);
        output.WriteLine($"REAL TYPOS fixed {fixedCount}/{typosTotal}, wrong {wrong.Count}, missed {missed.Count}, damaged {damaged.Count}/{cases.Count - typosTotal}");
        output.WriteLine("REAL damaged: " + string.Join(", ", damaged));
        output.WriteLine("REAL wrong: " + string.Join(", ", wrong));
        output.WriteLine("REAL missed: " + string.Join(", ", missed));
        // правильные слова и жаргон не портим вовсе; опечаток ловим не меньше, чем на Маке (2026-11-06: 126 из 144)
        Assert.True(damaged.Count == 0, "испортил: " + string.Join(", ", damaged));
        Assert.True(fixedCount >= 126, $"исправил только {fixedCount}");
    }
}
