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
