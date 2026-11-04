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
