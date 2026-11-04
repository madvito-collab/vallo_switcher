using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class AppSettingsTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "vallo-settings-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(dir, "Vallo Switcher", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Defaults()
    {
        var s = new AppSettings();
        Assert.True(s.AutoSwitchEnabled);
        Assert.Equal(
            ["windowsterminal.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "code.exe", "cursor.exe"],
            s.ExcludedApps);
        Assert.Equal(["javaw.exe"], s.SilentApps);
        Assert.Empty(s.LearnedWords);
        Assert.Equal(Hotkey.DefaultConvert, s.HotkeyConvert);
        Assert.False(s.DebugMode);
        Assert.False(s.FirstRunDone);
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var s = AppSettings.Load(FilePath);
        Assert.True(s.AutoSwitchEnabled);
        Assert.False(s.FirstRunDone);
        Assert.Equal(6, s.ExcludedApps.Count);
    }

    [Fact]
    public void BrokenFileGivesDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ это не json");
        var s = AppSettings.Load(FilePath);
        Assert.True(s.AutoSwitchEnabled);
        Assert.Equal(6, s.ExcludedApps.Count);
    }

    [Fact]
    public void NullJsonGivesDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "null");
        Assert.True(AppSettings.Load(FilePath).AutoSwitchEnabled);
    }

    [Fact]
    public void MissingFieldsKeepDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """{ "debugMode": true, "excludedApps": null }""");
        var s = AppSettings.Load(FilePath);
        Assert.True(s.DebugMode);
        Assert.True(s.AutoSwitchEnabled);
        Assert.Equal(6, s.ExcludedApps.Count);
        Assert.Equal(Hotkey.DefaultConvert, s.HotkeyConvert);
    }

    [Fact]
    public void FileFromEarlierBuildLoads()
    {
        // прошлая сборка не знала silentApps, а javaw.exe держала в исключениях
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """
            {
              "autoSwitchEnabled": false,
              "excludedApps": ["windowsterminal.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "code.exe", "cursor.exe", "javaw.exe"],
              "learnedWords": ["ёлка"],
              "debugMode": false,
              "firstRunDone": true
            }
            """);
        var s = AppSettings.Load(FilePath);
        Assert.False(s.AutoSwitchEnabled);
        Assert.True(s.FirstRunDone);
        Assert.Equal(7, s.ExcludedApps.Count);
        Assert.Equal(["javaw.exe"], s.SilentApps);
    }

    [Fact]
    public void NullSilentAppsGivesDefault()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """{ "silentApps": null }""");
        Assert.Equal(["javaw.exe"], AppSettings.Load(FilePath).SilentApps);
    }

    [Fact]
    public void SaveThenLoad()
    {
        var saved = new AppSettings
        {
            AutoSwitchEnabled = false,
            ExcludedApps = ["notepad.exe"],
            SilentApps = ["game.exe"],
            LearnedWords = ["ёлка", "github"],
            HotkeyConvert = new Hotkey.Combo(0x54, Ctrl: true, Alt: true, Shift: false),
            DebugMode = true,
            FirstRunDone = true,
        };
        saved.Save(FilePath);

        var s = AppSettings.Load(FilePath);
        Assert.False(s.AutoSwitchEnabled);
        Assert.Equal(["notepad.exe"], s.ExcludedApps);
        Assert.Equal(["game.exe"], s.SilentApps);
        Assert.Equal(["ёлка", "github"], s.LearnedWords);
        Assert.Equal(new Hotkey.Combo(0x54, true, true, false), s.HotkeyConvert);
        Assert.True(s.DebugMode);
        Assert.True(s.FirstRunDone);
    }

    [Fact]
    public void DefaultPathIsInAppData()
    {
        Assert.EndsWith(Path.Combine("Vallo Switcher", "settings.json"), AppSettings.DefaultPath);
    }
}
