using System.IO.Compression;
using Xunit;

namespace Vallo.Core.Tests;

/// Состояние модели запятых на странице и в «Основном».
public class ModelStateTests
{
    [Fact]
    public void PageShowsModelAndWarning()
    {
        var page = SettingsPage.State(new AppSettings(), false, "0.5", null, e => e, model: new ModelState.Downloading(0.42));
        var m = page["about"]!["model"]!;
        Assert.Equal("downloading", (string?)m["state"]);
        Assert.Equal(0.42, (double)m["progress"]!);
        Assert.Equal("228 МБ", (string?)m["size"]);
        Assert.False((bool)page["attention"]!["modelMissing"]!);
        Assert.True((bool)page["correct"]!["commasAvailable"]!);

        var missing = SettingsPage.State(new AppSettings(), false, "0.5", null, e => e);
        Assert.True((bool)missing["attention"]!["modelMissing"]!);
        var failed = SettingsPage.State(new AppSettings(), false, "0.5", null, e => e, model: new ModelState.Failed(CommaModelInfo.BadChecksum));
        Assert.Equal(CommaModelInfo.BadChecksum, (string?)failed["about"]!["model"]!["message"]);
        Assert.True((bool)failed["attention"]!["modelMissing"]!);
    }

    [Fact]
    public void CapitalizeOffByDefaultLikeMac()
    {
        Assert.False(new AppSettings().Capitalize);
        var s = SettingsPage.Apply(new AppSettings(), "capitalize", System.Text.Json.JsonDocument.Parse("true").RootElement);
        Assert.True(s.Capitalize);
    }

    [Fact]
    public void InstalledNeedsAllFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vallo-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Assert.False(CommaModelInfo.IsInstalled(dir));
        foreach (var f in new[] { "encoder_model.onnx", "decoder_model.onnx" }) File.WriteAllText(Path.Combine(dir, f), "");
        Assert.False(CommaModelInfo.IsInstalled(dir));
        File.WriteAllText(Path.Combine(dir, "tokenizer.json"), "{}");
        Assert.True(CommaModelInfo.IsInstalled(dir));
        Assert.IsType<ModelState.Ready>(new ModelStore(dir).State);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void DeleteRemovesModel()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vallo-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in new[] { "encoder_model.onnx", "decoder_model.onnx", "tokenizer.json" }) File.WriteAllText(Path.Combine(dir, f), "");
        var store = new ModelStore(dir);
        var replaced = false;
        store.ModelReplaced = () => replaced = true;
        store.Delete();
        Assert.IsType<ModelState.NotDownloaded>(store.State);
        Assert.False(Directory.Exists(dir));
        Assert.True(replaced);
    }

    /// Настоящее скачивание 228 МБ — только по просьбе: VALLO_MODEL_DOWNLOAD=1 dotnet test --filter RealDownload.
    [Fact]
    public async Task RealDownload()
    {
        if (Environment.GetEnvironmentVariable("VALLO_MODEL_DOWNLOAD") != "1") return;
        var dir = Path.Combine(Path.GetTempPath(), "vallo-model-real", CommaModelInfo.Name);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        var store = new ModelStore(dir);
        var seen = new List<double>();
        var done = new TaskCompletionSource();
        store.Changed = () =>
        {
            if (store.State is ModelState.Downloading d) lock (seen) seen.Add(d.Progress);
            if (store.State is ModelState.Ready or ModelState.Failed) done.TrySetResult();
        };
        store.Download();
        await done.Task.WaitAsync(TimeSpan.FromMinutes(20));
        Assert.IsType<ModelState.Ready>(store.State);
        Assert.True(CommaModelInfo.IsInstalled(dir));
        Assert.True(seen.Count > 50, $"процентов показали {seen.Count}");
        Assert.Contains(1.0, seen);
    }
}
