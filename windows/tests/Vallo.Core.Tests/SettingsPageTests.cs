using System.Text.Json;
using System.Text.Json.Nodes;
using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

/// Окно настроек — та же страница, что на Маке (settings-ui/). Протокол — settings-ui/src/bridge.ts
public class SettingsPageTests
{
    static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    static JsonObject State(AppSettings s, string? page = null) =>
        SettingsPage.State(s, launchAtLogin: true, version: "0.1", page: page, appName: exe => exe.Replace(".exe", ""));

    [Fact]
    public void StateHasOnlyWhatWindowsCanDo()
    {
        var st = State(new AppSettings());
        Assert.Equal("windows", (string?)st["platform"]);
        Assert.False((bool)st["correct"]!["available"]!);
        Assert.False((bool)st["integrations"]!["figma"]!);
        Assert.False((bool)st["about"]!["updates"]!);
        Assert.False((bool)st["canRecordHotkey"]!);
        Assert.False((bool)st["attention"]!["modelMissing"]!);
        Assert.True((bool)st["general"]!["launchAtLogin"]!);
    }

    [Fact]
    public void StateLayoutSection()
    {
        var st = State(new AppSettings { LearnedWords = ["ghbdtn"] });
        var layout = st["layout"]!;
        Assert.True((bool)layout["autoSwitch"]!);
        Assert.Equal("double:leftShift", (string?)layout["hotkey"]);
        Assert.Equal("ghbdtn", (string?)layout["words"]![0]);
        var first = layout["apps"]![0]!;
        Assert.Equal("windowsterminal.exe", (string?)first["id"]);
        Assert.Equal("windowsterminal", (string?)first["name"]);
        Assert.Equal(4, st["hotkeyOptions"]!.AsArray().Count);
        Assert.Equal("double:rightControl", (string?)st["hotkeyOptions"]![3]!["id"]);
    }

    [Fact]
    public void PageIsPassedThrough()
    {
        Assert.Equal("about", (string?)State(new AppSettings(), page: "about")["page"]);
        Assert.Null(State(new AppSettings())["page"]);
    }

    [Fact]
    public void ApplySwitchesAndLists()
    {
        var s = new AppSettings();
        s = SettingsPage.Apply(s, "autoSwitch", J("false"));
        s = SettingsPage.Apply(s, "debugMode", J("true"));
        s = SettingsPage.Apply(s, "apps.layout", J("""["Code.EXE"]"""));
        s = SettingsPage.Apply(s, "words.layout", J("""["vfvf"]"""));
        Assert.False(s.AutoSwitchEnabled);
        Assert.True(s.DebugMode);
        Assert.Equal(["code.exe"], s.ExcludedApps);
        Assert.Equal(["vfvf"], s.LearnedWords);
    }

    [Fact]
    public void ApplyHotkey()
    {
        var s = SettingsPage.Apply(new AppSettings(), "hotkey.convert", J("\"double:rightControl\""));
        Assert.Equal(new Hotkey.DoubleTap(ModifierKey.RightControl), s.HotkeyConvert);
        // непонятный вариант — ничего не меняем
        Assert.Equal(s, SettingsPage.Apply(s, "hotkey.convert", J("\"record\"")));
    }

    [Fact]
    public void UnknownKeyAndWrongTypeChangeNothing()
    {
        var s = new AppSettings();
        Assert.Equal(s, SettingsPage.Apply(s, "typography", J("true")));
        Assert.Equal(s, SettingsPage.Apply(s, "autoSwitch", J("\"да\"")));
    }

    [Fact]
    public void AddWordNormalizesAndSkipsDuplicates()
    {
        var s = SettingsPage.AddWord(new AppSettings(), "  Ёлка ");
        Assert.Equal(["елка"], s.LearnedWords);
        Assert.Equal(s, SettingsPage.AddWord(s, "елка"));
        Assert.Equal(s, SettingsPage.AddWord(s, "   "));
    }
}
