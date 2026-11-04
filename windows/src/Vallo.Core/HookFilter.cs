namespace Vallo.Core;

/// Что делать с событием клавиатурного перехвата.
public enum HookDecision
{
    /// Наше или чужое синтетическое — пропустить, никого не трогая.
    Ignore,
    /// Сочетание с Ctrl/Alt/Win — пропустить, память набора сбросить.
    ResetBuffer,
    /// Shift/Ctrl/Alt/Caps Lock сами по себе — в детектор двойного нажатия.
    Modifier,
    /// Обычная клавиша — в движок.
    Key,
}

/// Сортировка событий WH_KEYBOARD_LL. Без системных API.
public static class HookFilter
{
    /// «VALL» в dwExtraInfo — наши события.
    public const nuint OurMarker = 0x56414C4C;
    public const uint LLKHF_INJECTED = 0x10;

    /// `ctrl`/`alt`/`win` — что зажато; сама клавиша события может входить в это число.
    public static HookDecision Classify(uint vk, uint flags, nuint extraInfo, bool ctrl, bool alt, bool win)
    {
        if (extraInfo == OurMarker || (flags & LLKHF_INJECTED) != 0) return HookDecision.Ignore;
        bool isShift = vk is 0x10 or 0xA0 or 0xA1;
        bool isCtrl = vk is 0x11 or 0xA2 or 0xA3;
        bool isAlt = vk is 0x12 or 0xA4 or 0xA5;
        bool isWin = vk is 0x5B or 0x5C;
        if (isWin) return HookDecision.ResetBuffer;
        // зажатое, не считая самой клавиши (AltGr = Ctrl+Alt сюда же)
        bool held = (ctrl && !isCtrl) || (alt && !isAlt) || win;
        if (held) return HookDecision.ResetBuffer;
        if (isShift || isCtrl || isAlt || vk == 0x14) return HookDecision.Modifier;
        return HookDecision.Key;
    }
}
