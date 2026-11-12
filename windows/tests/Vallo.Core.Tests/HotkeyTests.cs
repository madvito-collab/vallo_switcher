using System.Text.Json;
using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class HotkeyTests
{
    [Fact]
    public void TwoQuickTapsFire()
    {
        var d = new DoubleTapDetector(ModifierKey.LeftShift);
        d.KeyDown(0);
        Assert.False(d.KeyUp(0.05));
        d.KeyDown(0.2);
        Assert.True(d.KeyUp(0.25));
    }

    [Fact]
    public void SlowSecondTapDoesNotFire()
    {
        var d = new DoubleTapDetector(ModifierKey.LeftShift);
        d.KeyDown(0);
        d.KeyUp(0.05);
        d.KeyDown(0.45);
        Assert.False(d.KeyUp(0.5));
    }

    [Fact]
    public void ShiftWithLetterIsNotTap()
    {
        var d = new DoubleTapDetector(ModifierKey.LeftShift);
        d.KeyDown(0);
        d.KeyUp(0.05);
        d.KeyDown(0.1);
        d.OtherInput(); // Shift+буква — заглавная
        Assert.False(d.KeyUp(0.2));
    }

    [Fact]
    public void KeyBetweenTapsCancels()
    {
        var d = new DoubleTapDetector(ModifierKey.LeftShift);
        d.KeyDown(0);
        d.KeyUp(0.05);
        d.OtherInput();
        d.KeyDown(0.1);
        Assert.False(d.KeyUp(0.15));
    }

    [Fact]
    public void ThreeTapsFireOnce()
    {
        var d = new DoubleTapDetector(ModifierKey.LeftShift);
        int fired = 0;
        for (int i = 0; i < 3; i++)
        {
            double t = i * 0.1;
            d.KeyDown(t);
            if (d.KeyUp(t + 0.03)) fired++;
        }
        Assert.Equal(1, fired);
    }

    [Fact]
    public void OtherModifierKeyWorks()
    {
        var d = new DoubleTapDetector(ModifierKey.RightControl);
        d.KeyDown(0);
        d.KeyUp(0.05);
        d.KeyDown(0.1);
        Assert.True(d.KeyUp(0.15));
    }

    [Fact]
    public void RoundTripJson()
    {
        Hotkey[] keys =
        {
            new Hotkey.DoubleTap(ModifierKey.LeftControl),
            new Hotkey.Combo(0x54, Ctrl: true, Alt: true, Shift: false),
        };
        foreach (var key in keys)
        {
            var json = JsonSerializer.Serialize(key);
            Assert.Equal(key, JsonSerializer.Deserialize<Hotkey>(json));
        }
    }

    [Fact]
    public void DefaultsDiffer()
    {
        Assert.Equal(new Hotkey.DoubleTap(ModifierKey.LeftShift), Hotkey.DefaultConvert);
        Assert.Equal(new Hotkey.DoubleTap(ModifierKey.RightShift), Hotkey.DefaultTypography);
        Assert.NotEqual(Hotkey.DefaultConvert, Hotkey.DefaultTypography);
    }

    [Fact]
    public void Phrases()
    {
        Assert.Equal("Нажмите левый Shift дважды", new Hotkey.DoubleTap(ModifierKey.LeftShift).PressInstruction);
        Assert.Equal("Нажмите правый Ctrl дважды", new Hotkey.DoubleTap(ModifierKey.RightControl).PressInstruction);
        Assert.Equal("по двойному нажатию правого Shift", new Hotkey.DoubleTap(ModifierKey.RightShift).ByPhrase);
        Assert.Equal("по двойному нажатию левого Ctrl", new Hotkey.DoubleTap(ModifierKey.LeftControl).ByPhrase);
        var combo = new Hotkey.Combo(0x54, Ctrl: true, Alt: true, Shift: false);
        Assert.Equal("Нажмите Ctrl+Alt+T", combo.PressInstruction);
        Assert.Equal("по сочетанию Ctrl+Alt+T", combo.ByPhrase);
    }

    [Fact]
    public void Titles()
    {
        Assert.Equal("Ctrl+Alt+T", new Hotkey.Combo(0x54, true, true, false).Title);
        Assert.Equal("Двойной правый Shift", new Hotkey.DoubleTap(ModifierKey.RightShift).Title);
        Assert.Equal("Двойной левый Ctrl", new Hotkey.DoubleTap(ModifierKey.LeftControl).Title);
        Assert.Equal("Двойной правый Ctrl", new Hotkey.DoubleTap(ModifierKey.RightControl).Title);
        Assert.Equal(new ushort[] { 0xA0, 0xA1, 0xA2, 0xA3 },
            Enum.GetValues<ModifierKey>().Select(k => k.Vk()).ToArray());
    }

    [Fact]
    public void ComboTitle()
    {
        Assert.Equal("Ctrl+Shift+K", new Hotkey.Combo(0x4B, Ctrl: true, Alt: false, Shift: true).Title);
        Assert.Equal("Ctrl+Пробел", new Hotkey.Combo(0x20, true, false, false).Title);
        Assert.Equal("F5", new Hotkey.Combo(0x74, false, false, false).Title);
    }
}

/// «Своё сочетание…»: запись в настройках и узнавание в перехвате.
public class HotkeyComboTests
{
    const ushort K = 0x4B, T = 0x54, C = 0x43, Tab = 0x09, F4 = 0x73, Esc = 0x1B;

    [Fact]
    public void RecordsCtrlOrAltCombos()
    {
        Assert.Equal((RecordStep.Accept, new Hotkey.Combo(K, true, true, false)), HotkeyCombo.Record(K, ctrl: true, alt: true, shift: false, win: false));
        Assert.Equal((RecordStep.Accept, new Hotkey.Combo(K, false, true, true)), HotkeyCombo.Record(K, false, true, true, false));
        Assert.Equal((RecordStep.Accept, new Hotkey.Combo(K, true, false, false)), HotkeyCombo.Record(K, true, false, false, false));
        // Ctrl+Alt+T — не стандартное, его можно
        Assert.Equal(RecordStep.Accept, HotkeyCombo.Record(T, true, true, false, false).Step);
        Assert.Equal(RecordStep.Ignore, HotkeyCombo.Record(C, true, false, true, false).Step);   // Ctrl+Shift+C — инструменты в браузерах
    }

    [Fact]
    public void IgnoresWhatWouldBreakTyping()
    {
        Assert.Equal(RecordStep.Ignore, HotkeyCombo.Record(K, false, false, false, false).Step);   // просто буква
        Assert.Equal(RecordStep.Ignore, HotkeyCombo.Record(K, false, false, true, false).Step);    // заглавная
        Assert.Equal(RecordStep.Ignore, HotkeyCombo.Record(C, true, false, false, false).Step);    // Ctrl+C
        Assert.Equal(RecordStep.Ignore, HotkeyCombo.Record(Tab, false, true, false, false).Step);  // Alt+Tab
        Assert.Equal(RecordStep.Ignore, HotkeyCombo.Record(F4, false, true, false, false).Step);   // Alt+F4
        Assert.Equal(RecordStep.Ignore, HotkeyCombo.Record(K, true, false, false, true).Step);     // с Win
        Assert.Equal(RecordStep.Ignore, HotkeyCombo.Record(0xA2, true, false, false, false).Step); // сам Ctrl
    }

    [Fact]
    public void EscCancels() => Assert.Equal(RecordStep.Cancel, HotkeyCombo.Record(Esc, false, false, false, false).Step);

    [Fact]
    public void MatchesExactModifiers()
    {
        var c = new Hotkey.Combo(K, true, true, false);
        Assert.True(c.Matches(K, ctrl: true, alt: true, shift: false, win: false));
        Assert.False(c.Matches(K, true, true, true, false));    // лишний Shift
        Assert.False(c.Matches(K, true, false, false, false));  // нет Alt
        Assert.False(c.Matches(T, true, true, false, false));
        Assert.False(c.Matches(K, true, true, false, true));    // с Win
    }

    [Fact]
    public void ComboTitleAndSavedInSettings()
    {
        var c = new Hotkey.Combo(K, true, true, false);
        Assert.Equal("Ctrl+Alt+K", c.Title);
        Assert.Equal("Нажмите Ctrl+Alt+K", c.PressInstruction);
        var s = new AppSettings { HotkeyConvert = c };
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s));
        Assert.Equal(c, back!.HotkeyConvert);
    }

    [Fact]
    public void RecordedComboGoesToSettingsUnlessTaken()
    {
        var c = new Hotkey.Combo(K, true, true, false);
        var (s, err) = SettingsPage.SetRecorded(new AppSettings(), "correct", c);
        Assert.Null(err);
        Assert.Equal(c, s.HotkeyCorrect);
        // то же сочетание для раскладки — нельзя
        var (same, err2) = SettingsPage.SetRecorded(s, "convert", c);
        Assert.Same(s, same);
        Assert.Equal(new HotkeyError("convert", "Это сочетание уже исправляет текст"), err2);
        var st = SettingsPage.State(s, false, "0.3", null, e => e, recording: "convert", hotkeyError: err2);
        Assert.True((bool)st["canRecordHotkey"]!);
        Assert.Equal("convert", (string?)st["recording"]);
        Assert.Equal("Это сочетание уже исправляет текст", (string?)st["hotkeyError"]!["text"]);
        Assert.Equal("custom", (string?)st["correct"]!["hotkey"]);
        Assert.Equal("Ctrl+Alt+K", (string?)st["correct"]!["hotkeyTitle"]);
    }
}
