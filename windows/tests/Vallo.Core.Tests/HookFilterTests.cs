using Vallo.Core;
using Xunit;

namespace Vallo.Core.Tests;

public class HookFilterTests
{
    const uint LLKHF_INJECTED = 0x10;

    static HookDecision Classify(uint vk, uint flags = 0, nuint extra = 0, bool ctrl = false, bool alt = false, bool win = false) =>
        HookFilter.Classify(vk, flags, extra, ctrl, alt, win);

    [Fact]
    public void InjectedEventsAreIgnored()
    {
        Assert.Equal(HookDecision.Ignore, Classify(0x51, extra: HookFilter.OurMarker));
        Assert.Equal(HookDecision.Ignore, Classify(0x51, flags: LLKHF_INJECTED));
        // наши же модификаторы — тоже мимо детектора двойного нажатия
        Assert.Equal(HookDecision.Ignore, Classify(0xA0, extra: HookFilter.OurMarker));
    }

    [Fact]
    public void AltGrResetsBuffer()
    {
        Assert.Equal(HookDecision.ResetBuffer, Classify(0x51, ctrl: true, alt: true));
    }

    [Fact]
    public void CtrlAltOrWinWithKeyResetsBuffer()
    {
        Assert.Equal(HookDecision.ResetBuffer, Classify(0x56, ctrl: true));
        Assert.Equal(HookDecision.ResetBuffer, Classify(0x09, alt: true));
        Assert.Equal(HookDecision.ResetBuffer, Classify(0x44, win: true));
    }

    [Fact]
    public void ShiftIsModifier()
    {
        Assert.Equal(HookDecision.Modifier, Classify(0xA0));
        Assert.Equal(HookDecision.Modifier, Classify(0xA1));
    }

    [Fact]
    public void CtrlAndAltAloneAreModifiers()
    {
        // сам Ctrl зажат — это и есть его нажатие, не «Ctrl + клавиша»
        Assert.Equal(HookDecision.Modifier, Classify(0xA2, ctrl: true));
        Assert.Equal(HookDecision.Modifier, Classify(0xA5, alt: true));
    }

    [Fact]
    public void ModifierWithAnotherModifierHeldResetsBuffer()
    {
        // Ctrl+Shift — системное переключение раскладки, не «чистый» Shift
        Assert.Equal(HookDecision.ResetBuffer, Classify(0xA0, ctrl: true));
        Assert.Equal(HookDecision.ResetBuffer, Classify(0xA0, alt: true));
    }

    [Fact]
    public void WinKeyResetsBuffer()
    {
        Assert.Equal(HookDecision.ResetBuffer, Classify(0x5B, win: true));
    }

    [Fact]
    public void CapsLockIsModifier()
    {
        // как на Маке: Caps Lock посреди слова память набора не сбрасывает
        Assert.Equal(HookDecision.Modifier, Classify(0x14));
    }

    [Fact]
    public void LetterIsKey()
    {
        Assert.Equal(HookDecision.Key, Classify(0x51));
        Assert.Equal(HookDecision.Key, Classify(0x20));
        Assert.Equal(HookDecision.Key, Classify(0xBC));
    }
}
