using Xunit;
using Xunit.Abstractions;

namespace Vallo.Core.Tests;

/// Модель запятых против эталона (`Tests/Data/comma-golden.tsv`: официальный токенайзер HuggingFace + onnxruntime).
/// Модели в репозитории нет (470 МБ) — тесты идут, если она скачана: VALLO_MODEL_DIR=<папка sage-fredt5-95m>.
public class CommaModelTests(ITestOutputHelper output)
{
    static readonly string? Dir = Environment.GetEnvironmentVariable("VALLO_MODEL_DIR") is { } d && CommaModelInfo.IsInstalled(d) ? d : null;
    static readonly Lazy<CommaModel?> Model = new(() => Dir is null ? null : CommaModel.Load(Dir));

    static List<(string Phrase, long[] Ids, string Answer)> Golden() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData", "comma-golden.tsv"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split('\t'))
            .Select(p => (p[0], p[1].Split(' ').Select(long.Parse).ToArray(), p[2]))
            .ToList();

    [Fact]
    public void TokensMatchOfficialTokenizer()
    {
        if (Dir is null) return;
        var tokenizer = Gpt2Tokenizer.Load(Path.Combine(Dir, "tokenizer.json"));
        foreach (var (phrase, ids, _) in Golden()) Assert.Equal(ids, tokenizer.Encode(phrase));
    }

    [Fact]
    public void AnswersMatchReference()
    {
        if (Model.Value is not { } model) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var golden = Golden();
        foreach (var (phrase, _, answer) in golden) Assert.Equal(answer, model.Run(phrase));
        output.WriteLine($"{golden.Count} фраз, {sw.ElapsedMilliseconds / golden.Count} мс на фразу");
    }

    [Fact]
    public void DecodeRoundTrip()
    {
        if (Dir is null) return;
        var tokenizer = Gpt2Tokenizer.Load(Path.Combine(Dir, "tokenizer.json"));
        const string text = "Ёлки-палки, 5000 ₽ и «кавычки» 😄";
        Assert.Equal(text, tokenizer.Decode(tokenizer.Encode(text).Skip(1).Select(i => (int)i)));
    }
}

/// Перенос `Tests/ValloCoreTests/DraftLookupTests.swift`.
public class DraftLookupTests
{
    static readonly long[] Source = [10, 11, 12, 13, 14, 15, 2];

    [Fact]
    public void EmptyOutputDraftsSourceStart() => Assert.Equal([10L, 11, 12], DraftLookup.Draft([], Source, limit: 3));

    [Fact]
    public void ContinuesAfterMatchedTail() => Assert.Equal([13L, 14, 15, 2], DraftLookup.Draft([10, 11, 12], Source));

    [Fact]
    public void SkipsInsertedComma() =>
        // 99 — запятая, которой во входе нет: черновик продолжается со следующего слова
        Assert.Equal([13L, 14, 15, 2], DraftLookup.Draft([10, 11, 12, 99], Source));

    [Fact]
    public void SingleTokenTailIsEnough() => Assert.Equal([14L, 15, 2], DraftLookup.Draft([77, 13], Source));

    [Fact]
    public void NothingMatchesNoDraft() => Assert.Empty(DraftLookup.Draft([70, 71, 72, 73, 74], Source));

    [Fact]
    public void EndOfSourceNoDraft() => Assert.Empty(DraftLookup.Draft([14, 15, 2], Source));
}

/// Сервис запятых: предложения, без модели, с моделью (если скачана), выгрузка.
public class CommaServiceTests
{
    static readonly string? Dir = Environment.GetEnvironmentVariable("VALLO_MODEL_DIR") is { } d && CommaModelInfo.IsInstalled(d) ? d : null;

    [Fact]
    public void SplitsSentences()
    {
        Assert.Equal(["Привет.", "Как дела?", "Норм"], CommaService.Sentences("Привет. Как дела? Норм"));
        Assert.Equal(["Привет.", "Как дела?"], CommaService.Sentences("Привет.   Как дела?"));
        Assert.Equal(["в 15.00 встреча"], CommaService.Sentences("в 15.00 встреча"));
        Assert.Equal(["  "], CommaService.Sentences("  "));
    }

    [Fact]
    public async Task NoModelNoCommas()
    {
        using var service = new CommaService(Path.Combine(Path.GetTempPath(), "vallo-no-model-" + Guid.NewGuid().ToString("N")));
        Assert.Null(await service.FixAsync("я думаю что это идея", Spelling.Typos, false));
    }

    [Fact]
    public async Task CommasWithModelAndUnload()
    {
        if (Dir is null) return;
        using var service = new CommaService(Dir);
        // строки и пустая строка между ними — как были; запятые — в каждой
        Assert.Equal("я думаю, что это хорошая идея\n\nспасибо большое, ты лучший",
            await service.FixAsync("я думаю что это хорошая идея\n\nспасибо большое ты лучший", Spelling.Typos, capitalize: false));
        Assert.Equal("Спасибо большое, ты лучший.", await service.FixAsync("спасибо большое ты лучший", Spelling.Typos, capitalize: true));
        Assert.True(service.IsLoaded);
        service.Unload();
        Assert.False(service.IsLoaded);
    }

    [Fact]
    public async Task BrokenModelReported()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vallo-broken-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in new[] { "encoder_model.onnx", "decoder_model.onnx", "tokenizer.json" }) File.WriteAllText(Path.Combine(dir, f), "битый");
        using var service = new CommaService(dir);
        var failed = false;
        service.LoadFailed = () => failed = true;
        Assert.Null(await service.FixAsync("я думаю что это идея", Spelling.Typos, false));
        Assert.True(failed);
        Directory.Delete(dir, true);
    }
}
