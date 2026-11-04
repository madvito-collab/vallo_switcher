namespace Vallo.Core;

/// Одно нажатие: физическая клавиша (Windows VK) + состояние Shift и Caps Lock.
/// Храним именно нажатия, а не буквы — так одну и ту же клавишу можно
/// «прочитать» в любой раскладке.
public readonly record struct KeyStroke(ushort KeyCode, bool Shift = false, bool CapsLock = false);
