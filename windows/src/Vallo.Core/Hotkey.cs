using System.Text.Json.Serialization;

namespace Vallo.Core;

/// Модификатор, который можно нажать дважды.
[JsonConverter(typeof(JsonStringEnumConverter<ModifierKey>))]
public enum ModifierKey { LeftShift, RightShift, LeftControl, RightControl }

public static class ModifierKeyExtensions
{
    /// Windows VK-код.
    public static ushort Vk(this ModifierKey key) => key switch
    {
        ModifierKey.LeftShift => 0xA0,
        ModifierKey.RightShift => 0xA1,
        ModifierKey.LeftControl => 0xA2,
        ModifierKey.RightControl => 0xA3,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    /// «левый Shift» — для фраз «Нажмите левый Shift дважды», «по двойному нажатию левого Shift».
    internal static (string Nominative, string Genitive) KeyName(this ModifierKey key) => key switch
    {
        ModifierKey.LeftShift => ("левый Shift", "левого Shift"),
        ModifierKey.RightShift => ("правый Shift", "правого Shift"),
        ModifierKey.LeftControl => ("левый Ctrl", "левого Ctrl"),
        ModifierKey.RightControl => ("правый Ctrl", "правого Ctrl"),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    public static string Title(this ModifierKey key) => key switch
    {
        ModifierKey.LeftShift => "Двойной левый Shift",
        ModifierKey.RightShift => "Двойной правый Shift",
        ModifierKey.LeftControl => "Двойной левый Ctrl",
        ModifierKey.RightControl => "Двойной правый Ctrl",
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };
}

/// Сочетание для действия: двойное нажатие модификатора или своё сочетание клавиш.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DoubleTap), "doubleTap")]
[JsonDerivedType(typeof(Combo), "combo")]
public abstract record Hotkey
{
    public sealed record DoubleTap(ModifierKey Key) : Hotkey;
    public sealed record Combo(ushort Vk, bool Ctrl, bool Alt, bool Shift) : Hotkey;

    public static readonly Hotkey DefaultConvert = new DoubleTap(ModifierKey.LeftShift);
    public static readonly Hotkey DefaultTypography = new DoubleTap(ModifierKey.RightShift);

    /// «Нажмите левый Shift дважды» / «Нажмите Ctrl+Alt+T».
    [JsonIgnore]
    public string PressInstruction => this switch
    {
        DoubleTap d => $"Нажмите {d.Key.KeyName().Nominative} дважды",
        _ => $"Нажмите {Title}",
    };

    /// «по двойному нажатию правого Shift» / «по сочетанию Ctrl+Alt+T».
    [JsonIgnore]
    public string ByPhrase => this switch
    {
        DoubleTap d => $"по двойному нажатию {d.Key.KeyName().Genitive}",
        _ => $"по сочетанию {Title}",
    };

    [JsonIgnore]
    public string Title => this switch
    {
        DoubleTap d => d.Key.Title(),
        Combo c => ComboTitle(c),
        _ => throw new InvalidOperationException(),
    };

    static string ComboTitle(Combo c)
    {
        var parts = new List<string>();
        if (c.Ctrl) parts.Add("Ctrl");
        if (c.Alt) parts.Add("Alt");
        if (c.Shift) parts.Add("Shift");
        parts.Add(KeyName(c.Vk));
        return string.Join("+", parts);
    }

    // Подпись клавиши: буквы и цифры — по VK (совпадают с ASCII), остальное — из небольшой карты.
    static string KeyName(ushort vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        return vk switch
        {
            0x20 => "Пробел",
            0x0D => "Enter",
            0x09 => "Tab",
            0x1B => "Esc",
            0x08 => "Backspace",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x25 => "←",
            0x26 => "↑",
            0x27 => "→",
            0x28 => "↓",
            _ => $"#{vk}",
        };
    }
}

/// Ловит двойное нажатие модификатора: два «чистых» нажатия подряд (без других клавиш),
/// второе начинается не позже `Window` после отпускания первого.
public struct DoubleTapDetector
{
    public const double Window = 0.3;

    public ModifierKey Key;
    // Модификатор сейчас зажат и пока «чистый».
    bool downClean;
    // Когда отпустили первое чистое нажатие.
    double? firstTapUp;

    public DoubleTapDetector(ModifierKey key)
    {
        Key = key;
        downClean = false;
        firstTapUp = null;
    }

    public void KeyDown(double t)
    {
        if (firstTapUp is double up && t - up > Window) firstTapUp = null;
        downClean = true;
    }

    /// true — сработало двойное нажатие.
    public bool KeyUp(double t)
    {
        if (!downClean) return false;
        downClean = false;
        if (firstTapUp != null)
        {
            firstTapUp = null;
            return true;
        }
        firstTapUp = t;
        return false;
    }

    /// Любая другая клавиша, другой модификатор, клик.
    public void OtherInput()
    {
        downClean = false;
        firstTapUp = null;
    }
}

/// Что делать с нажатием, пока в настройках записывают «Своё сочетание…».
public enum RecordStep { Ignore, Cancel, Accept }

/// Запись и узнавание своего сочетания. Как на Маке: нужен Ctrl или Alt (иначе сочетание перехватывало бы
/// обычные буквы), стандартные Ctrl+C, Ctrl+V и т. п. не берём — ждём другое; Esc — отмена. Win не берём:
/// его сочетания — системные.
public static class HotkeyCombo
{
    /// Ctrl+клавиша без Alt — стандартные сочетания программ: C V X Z Y A S F P N O W T, Tab, Пробел.
    static readonly HashSet<ushort> CtrlReserved =
        [0x43, 0x56, 0x58, 0x5A, 0x59, 0x41, 0x53, 0x46, 0x50, 0x4E, 0x4F, 0x57, 0x54, 0x09, 0x20];
    /// Alt+клавиша без Ctrl — системные: Tab (переключение окон), F4 (закрыть), Пробел (меню окна), Esc.
    static readonly HashSet<ushort> AltReserved = [0x09, 0x73, 0x20, 0x1B];
    const ushort Esc = 0x1B;

    public static bool IsModifierVk(ushort vk) => vk is >= 0x10 and <= 0x12 or >= 0xA0 and <= 0xA5 or 0x5B or 0x5C or 0x14;

    public static (RecordStep Step, Hotkey.Combo? Combo) Record(ushort vk, bool ctrl, bool alt, bool shift, bool win)
    {
        if (vk == Esc && !ctrl && !alt && !shift && !win) return (RecordStep.Cancel, null);
        if (IsModifierVk(vk) || win || (!ctrl && !alt)) return (RecordStep.Ignore, null);
        if (ctrl && !alt && CtrlReserved.Contains(vk)) return (RecordStep.Ignore, null);
        if (alt && !ctrl && AltReserved.Contains(vk)) return (RecordStep.Ignore, null);
        return (RecordStep.Accept, new Hotkey.Combo(vk, ctrl, alt, shift));
    }

    /// Это нажатие — сочетание `c`: та же клавиша и ровно те же модификаторы (Ctrl и Alt — любой из двух).
    public static bool Matches(this Hotkey.Combo c, ushort vk, bool ctrl, bool alt, bool shift, bool win) =>
        !win && c.Vk == vk && c.Ctrl == ctrl && c.Alt == alt && c.Shift == shift;
}
