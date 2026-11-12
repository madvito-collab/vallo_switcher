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
        // исправления есть все: типографика, смайлики, опечатки, «ё», запятые
        Assert.True((bool)st["correct"]!["available"]!);
        Assert.True((bool)st["correct"]!["typosAvailable"]!);
        Assert.True((bool)st["correct"]!["commasAvailable"]!);
        Assert.Equal("double:rightShift", (string?)st["correct"]!["hotkey"]);
        Assert.True((bool)st["correct"]!["typography"]!);
        Assert.Equal("millennial", (string?)st["correct"]!["emoji"]!["preset"]);
        Assert.False((bool)st["integrations"]!["figma"]!);
        Assert.False((bool)st["about"]!["updates"]!);
        Assert.True((bool)st["canRecordHotkey"]!);   // «Своё сочетание…» есть и на Windows
        Assert.True((bool)st["attention"]!["modelMissing"]!);   // модель не скачана — предупреждение, как на Маке
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

    [Fact]
    public void TypographySettingsApply()
    {
        var s = new AppSettings();
        s = SettingsPage.Apply(s, "typography", J("false"));
        s = SettingsPage.Apply(s, "emoji.preset", J("\"zoomer\""));
        s = SettingsPage.Apply(s, "emoji.custom", J("""[{"trigger":"))","emoji":"🫶"}]"""));
        s = SettingsPage.Apply(s, "apps.correct", J("""["Code.EXE"]"""));
        Assert.False(s.TypographyLive);
        Assert.Equal("zoomer", s.EmojiPreset);
        Assert.Equal([new EmojiPair("))", "🫶")], s.EmojiCustom);
        Assert.Equal(["code.exe"], s.TypographyExcludedApps);
        Assert.Equal(s, SettingsPage.Apply(s, "emoji.preset", J("\"boomer\"")));
    }

    [Fact]
    public void CorrectHotkeyOffAndNoConflicts()
    {
        var s = SettingsPage.Apply(new AppSettings(), "hotkey.correct", J("\"off\""));
        Assert.Null(s.HotkeyCorrect);
        s = SettingsPage.Apply(s, "hotkey.correct", J("\"double:rightControl\""));
        Assert.Equal(new Hotkey.DoubleTap(ModifierKey.RightControl), s.HotkeyCorrect);
        // одно сочетание на два действия не даём
        Assert.Equal(s, SettingsPage.Apply(s, "hotkey.convert", J("\"double:rightControl\"")));
        Assert.Equal(s, SettingsPage.Apply(s, "hotkey.correct", J("\"double:leftShift\"")));
    }

    [Fact]
    public void EmojiSetFromSettings()
    {
        Assert.Same(EmojiBrackets.Millennial, new AppSettings().EmojiBrackets());
        Assert.Null((new AppSettings { EmojiPreset = "off" }).EmojiBrackets());
        var mine = new AppSettings { EmojiPreset = "custom", EmojiCustom = [new("))", "🫶"), new("", "x")] }.EmojiBrackets()!;
        Assert.Equal("🫶", mine.Pairs["))"]);
        Assert.Single(mine.Pairs);
    }

    [Fact]
    public void AddAppsToCorrectList()
    {
        var s = SettingsPage.AddApps(new AppSettings { TypographyExcludedApps = [] }, [@"C:\Apps\Figma.exe"], "correct");
        Assert.Equal(["figma.exe"], s.TypographyExcludedApps);
    }
}
