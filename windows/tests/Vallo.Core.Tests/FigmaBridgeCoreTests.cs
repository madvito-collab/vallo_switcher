using System.Text;
using Xunit;

namespace Vallo.Core.Tests;

/// Перенос `Tests/ValloCoreTests/FigmaBridgeCoreTests.swift` и проверки пакета слоёв.
public class FigmaBridgeCoreTests
{
    const string Nb = "\u00A0";
    static readonly string Ls = ((char)0x2028).ToString();

    static FigmaMessage? Msg(string json) => FigmaMessage.Decode(json);

    /// Применить правки к старой строке (с конца, в единицах UTF-16) — как это делает плагин.
    static string Apply(IEnumerable<TextEdit> edits, string old)
    {
        var s = new StringBuilder(old);
        foreach (var e in edits.Reverse()) s.Remove(e.Start, e.End - e.Start).Insert(e.Start, e.Insert);
        return s.ToString();
    }

    // MARK: - Сообщения

    [Fact]
    public void HelloDecodes()
    {
        Assert.Equal(new FigmaMessage.Hello(1, "Лендинг", false), Msg("""{"type":"hello","version":1,"file":"Лендинг"}"""));
        Assert.Equal(new FigmaMessage.Hello(1, "Лендинг", true), Msg("""{"type":"hello","version":1,"file":"Лендинг","fresh":true}"""));
    }

    [Fact]
    public void LayersModeDecodes()
    {
        var m = Assert.IsType<FigmaMessage.Mode>(Msg("""{"type":"mode","id":7,"mode":"layers","texts":[{"id":"1:2","text":"а"},{"id":"1:3","text":"б"}],"skipped":3}"""));
        Assert.Equal(7, m.Id);
        Assert.Equal(3, m.Skipped);
        Assert.Equal([new FigmaText("1:2", "а"), new FigmaText("1:3", "б")], Assert.IsType<FigmaMode.Layers>(m.Kind).Texts);
        Assert.Equal(new FigmaMessage.Mode(8, new FigmaMode.Text(), 0), Msg("""{"type":"mode","id":8,"mode":"text"}"""));
        Assert.Equal(new FigmaMessage.Mode(9, new FigmaMode.Empty(), 0), Msg("""{"type":"mode","id":9,"mode":"empty"}"""));
        Assert.Equal(new FigmaMessage.Done(9), Msg("""{"type":"done","id":9}"""));
    }

    [Fact]
    public void ResultRoundTrip()
    {
        var stats = new FixStats { Commas = 2, Layers = 1 };
        var m = new FigmaMessage.Result(3, [new FigmaTextEdits("1:2", [new TextEdit(1, 2, Nb)])], stats, 1);
        var back = Assert.IsType<FigmaMessage.Result>(FigmaMessage.Decode(m.Encode()));
        Assert.Equal(3, back.Id);
        Assert.Equal(1, back.Skipped);
        Assert.Equal(stats, back.Stats);
        Assert.Equal("1:2", Assert.Single(back.Edits).Id);
        Assert.Equal([new TextEdit(1, 2, Nb)], back.Edits[0].Edits);
        Assert.Equal(new FigmaMessage.Press(5), FigmaMessage.Decode(new FigmaMessage.Press(5).Encode()));
    }

    [Fact]
    public void ResultWireMatchesPlugin()
    {
        // плагин читает именно эти имена полей (figma-plugin/code.js)
        var json = new FigmaMessage.Result(3, [new FigmaTextEdits("1:2", [new TextEdit(0, 1, "ё")])], new FixStats { Yo = 1, Layers = 1 }, 0).Encode();
        Assert.Equal("""{"edits":[{"id":"1:2","edits":[{"start":0,"end":1,"insert":"ё"}]}],"id":3,"skipped":0,"stats":{"commas":0,"typos":0,"yo":1,"nbsp":0,"dashes":0,"quotes":0,"other":0,"layers":1},"type":"result"}""", json);
        Assert.Equal("""{"id":4,"type":"press"}""", new FigmaMessage.Press(4).Encode());
    }

    [Fact]
    public void GarbageIsNull()
    {
        Assert.Null(FigmaMessage.Decode("{"));
        Assert.Null(Msg("""{"type":"zzz"}"""));
        Assert.Null(Msg("""{"type":"hello","version":"1","file":"x"}"""));
        Assert.Null(Msg("""{"type":"mode","id":1,"mode":"layers","texts":[{"id":"1:2"}]}"""));
        Assert.Null(Msg("[]"));
    }

    // MARK: - Правки

    [Fact]
    public void DiffEqualIsEmpty() => Assert.Empty(TextDiff.Edits("abc", "abc"));

    [Fact]
    public void DiffLocalNbsp()
    {
        var edits = TextDiff.Edits("в доме и в саду", $"в{Nb}доме и{Nb}в{Nb}саду");
        Assert.Equal(3, edits.Count);
        Assert.All(edits, e => { Assert.Equal(1, e.End - e.Start); Assert.Equal(Nb, e.Insert); });
    }

    [Fact]
    public void DiffUtf16Offsets() =>
        Assert.Equal([3, 6], TextDiff.Edits("👍 \"да\"", "👍 «да»").Select(e => e.Start));

    [Theory]
    [InlineData("в доме и в саду", "в\u00A0доме и\u00A0в\u00A0саду")]
    [InlineData("мир", "Новый мир")]
    [InlineData("конец...", "конец")]
    [InlineData("abc", "xyz")]
    [InlineData("👍 \"да\" - нет", "👍 «да»\u00A0— нет")]
    [InlineData("", "текст")]
    [InlineData("текст", "")]
    [InlineData("😄😄", "😄🙂😄")]
    public void ApplyReproducesNew(string old, string @new) =>
        Assert.Equal(@new, Apply(TextDiff.Edits(old, @new), old));

    [Fact]
    public void LongLayerStillLocalEdits()
    {
        // длинный слой (за пределом таблицы LCS) — правки всё равно точечные, иначе слетит оформление
        var line = string.Concat(Enumerable.Repeat("слово ", 300));
        var old = "\"начало\" " + line + "\n" + line + "\n" + line + "конец - да";
        var @new = "«начало» " + line + "\n" + line + "\n" + line + $"конец{Nb}— да";
        var edits = TextDiff.Edits(old, @new);
        Assert.Equal(@new, Apply(edits, old));
        Assert.All(edits, e => Assert.True(e.End - e.Start <= 3));
    }

    [Fact]
    public void EditsAreSortedAndDisjoint()
    {
        var edits = TextDiff.Edits("a - b \"c\" ...", $"a{Nb}— b «c» …");
        foreach (var (a, b) in edits.Zip(edits.Skip(1))) Assert.True(a.End <= b.Start);
    }

    // MARK: - Счётчики

    [Fact]
    public void Counters()
    {
        Assert.Equal(1, FixCounter.Commas("а б", "а, б"));
        Assert.Equal(1, FixCounter.Yo("еще", "ещё"));
        Assert.Equal(1, FixCounter.Typos("помоему тут", "по-моему тут"));
        Assert.Equal(0, FixCounter.Typos("всё тут", "всё тут"));
        Assert.Equal((1, 1, 2, 0), FixCounter.Typography("a - b \"c\"", $"a{Nb}— b «c»"));
        Assert.Equal(1, FixCounter.Typography("ну...", "ну…").Other);
    }

    [Fact]
    public void StatsAdd()
    {
        var a = new FixStats();
        Assert.True(a.IsEmpty);
        var b = new FixStats { Yo = 2, Layers = 1 };
        a.Add(b);
        a.Add(b);
        Assert.True(a.Yo == 4 && a.Layers == 2 && !a.IsEmpty);
        Assert.True(new FixStats { Layers = 3 }.IsEmpty);
    }

    // MARK: - Конвейер для слоя

    [Fact]
    public void PipelineKeepsCaseAndNoPeriod()
    {
        // в Фигме модель вызывается с capitalize: false — ни заглавной, ни точки в конце
        var merged = CommaFilter.Merge("привет как дела", "Привет, как дела.", Spelling.Typos, capitalize: false);
        var (text, stats) = FixPipeline.Finish("привет как дела", "привет как дела", "привет как дела", merged);
        Assert.Equal("привет, как дела", text);
        Assert.Equal(1, stats.Commas);
    }

    [Fact]
    public void PipelineKeepsLineSeparators()
    {
        var original = "Заголовок\nпервая строка" + Ls + "вторая";
        var (text, _) = FixPipeline.Finish(original, original, original, null);
        Assert.Contains("\n", text);
        Assert.Contains(Ls, text);
    }

    [Fact]
    public void PipelineStats()
    {
        var (text, stats) = FixPipeline.Finish("помоему еще - да", "по-моему еще - да", "по-моему ещё - да", null);
        Assert.Equal($"по-моему ещё{Nb}— да", text);
        Assert.True(stats.Typos == 1 && stats.Yo == 1 && stats.Dashes == 1 && stats.Nbsp >= 1 && stats.Layers == 1);
        var (same, none) = FixPipeline.Finish("да", "да", "да", null);
        Assert.Equal("да", same);
        Assert.True(none.IsEmpty && none.Layers == 0);
    }
}

/// Пакет слоёв без модели запятых (с моделью — в CommaModelTests, если она скачана).
public class FigmaBatchTests
{
    static FigmaBatch NoModel() => new(new CommaService(Path.Combine(Path.GetTempPath(), "vallo-no-model")));

    [Fact]
    public async Task EditsOnlyChangedLayersWithStats()
    {
        var batch = NoModel();
        var texts = new[] { new FigmaText("1", "помоему еще - да"), new FigmaText("2", "Да"), new FigmaText("3", "помоему еще - да") };
        var r = await batch.RunAsync(texts, Spelling.Typos, Spelling.Yo, yoEnabled: true);
        var (edits, stats) = r!.Value;
        Assert.Equal(["1", "3"], edits.Select(e => e.Id));
        var fixedText = $"по-моему ещё\u00A0— да";
        var e1 = edits[0].Edits;
        var s = new StringBuilder("помоему еще - да");
        foreach (var e in e1.Reverse()) s.Remove(e.Start, e.End - e.Start).Insert(e.Start, e.Insert);
        Assert.Equal(fixedText, s.ToString());
        // одинаковые тексты считаются оба — как на Маке
        Assert.Equal(2, stats.Layers);
        Assert.Equal(2, stats.Yo);
    }

    [Fact]
    public async Task YoSettingRespected()
    {
        var batch = NoModel();
        var r = await batch.RunAsync([new FigmaText("1", "еще")], Spelling.Typos, Spelling.Yo, yoEnabled: false);
        Assert.Empty(r!.Value.Edits);
        // настройка поменялась — память ответов не мешает
        r = await batch.RunAsync([new FigmaText("1", "еще")], Spelling.Typos, Spelling.Yo, yoEnabled: true);
        Assert.Single(r!.Value.Edits);
    }

    [Fact]
    public async Task CommasWithModelInParallelAndRemembered()
    {
        var dir = Environment.GetEnvironmentVariable("VALLO_MODEL_DIR") is { } d && CommaModelInfo.IsInstalled(d) ? d : null;
        if (dir is null) return;
        using var service = new CommaService(dir);
        var batch = new FigmaBatch(service);
        string[] phrases = ["я думаю что это хорошая идея", "спасибо большое ты лучший", "короче я думаю что это идея",
                            "если будет время приходи", "привет как дела", "Заголовок"];
        var texts = phrases.Select((t, i) => new FigmaText(i.ToString(), t)).ToList();
        var (edits, stats) = (await batch.RunAsync(texts, Spelling.Typos, Spelling.Yo, yoEnabled: true))!.Value;
        // без заглавной и точки, «Заголовок» не трогаем
        Assert.Equal(5, edits.Count);
        Assert.DoesNotContain(edits, e => e.Id == "5");
        Assert.True(stats.Commas >= 5);
        var first = edits.Single(e => e.Id == "0").Edits;
        Assert.Equal([new TextEdit(7, 7, ",")], first);   // «я думаю|, что…»
        // второй раз — из памяти, модель не нужна
        service.Unload();
        var again = (await batch.RunAsync(texts, Spelling.Typos, Spelling.Yo, yoEnabled: true))!.Value;
        Assert.False(service.IsLoaded);
        Assert.Equal(stats, again.Stats);
    }

    [Fact]
    public void TwoWords()
    {
        Assert.True(FigmaBatch.HasTwoWords("привет мир"));
        Assert.False(FigmaBatch.HasTwoWords("привет"));
        Assert.False(FigmaBatch.HasTwoWords("привет — 2024"));
    }
}
