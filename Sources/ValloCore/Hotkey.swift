import Foundation

/// Модификатор, который можно нажать дважды.
public enum ModifierKey: String, Codable, CaseIterable, Sendable {
    case leftShift, rightShift, leftOption, rightOption, leftControl

    public var keyCode: UInt16 {
        switch self {
        case .leftShift: 56
        case .rightShift: 60
        case .leftOption: 58
        case .rightOption: 61
        case .leftControl: 59
        }
    }

    public var title: String {
        switch self {
        case .leftShift: "Двойной левый Shift"
        case .rightShift: "Двойной правый Shift"
        case .leftOption: "Двойной левый Option"
        case .rightOption: "Двойной правый Option"
        case .leftControl: "Двойной Control"
        }
    }
}

/// Сочетание для действия: двойное нажатие модификатора или своё сочетание клавиш.
public enum Hotkey: Codable, Equatable, Sendable {
    case doubleTap(ModifierKey)
    case combo(keyCode: UInt16, command: Bool, option: Bool, control: Bool, shift: Bool)

    public static let defaultConvert: Hotkey = .doubleTap(.leftShift)
    public static let defaultTypography: Hotkey = .doubleTap(.rightShift)

    public var title: String {
        switch self {
        case .doubleTap(let key):
            return key.title
        case let .combo(keyCode, command, option, control, shift):
            var s = ""
            if control { s += "⌃" }
            if option { s += "⌥" }
            if shift { s += "⇧" }
            if command { s += "⌘" }
            return s + (Hotkey.keyNames[keyCode] ?? "#\(keyCode)")
        }
    }

    /// Названия клавиш по коду (основной блок ANSI) — только для подписи в настройках.
    static let keyNames: [UInt16: String] = [
        0: "A", 1: "S", 2: "D", 3: "F", 4: "H", 5: "G", 6: "Z", 7: "X", 8: "C", 9: "V", 11: "B", 12: "Q",
        13: "W", 14: "E", 15: "R", 16: "Y", 17: "T", 18: "1", 19: "2", 20: "3", 21: "4", 22: "6", 23: "5",
        25: "9", 26: "7", 28: "8", 29: "0", 31: "O", 32: "U", 34: "I", 35: "P", 37: "L", 38: "J", 40: "K",
        45: "N", 46: "M", 49: "Пробел", 36: "↩", 48: "⇥", 53: "Esc",
        122: "F1", 120: "F2", 99: "F3", 118: "F4", 96: "F5", 97: "F6", 98: "F7", 100: "F8",
        101: "F9", 109: "F10", 103: "F11", 111: "F12",
    ]
}

/// Ловит двойное нажатие модификатора: два «чистых» нажатия подряд (без других клавиш),
/// второе начинается не позже `window` после отпускания первого.
public struct DoubleTapDetector: Sendable {
    public static let window: TimeInterval = 0.3

    public var key: ModifierKey
    /// Модификатор сейчас зажат и пока «чистый».
    private var downClean = false
    /// Когда отпустили первое чистое нажатие.
    private var firstTapUp: TimeInterval?

    public init(key: ModifierKey) {
        self.key = key
    }

    public mutating func keyDown(at t: TimeInterval) {
        if let up = firstTapUp, t - up > DoubleTapDetector.window { firstTapUp = nil }
        downClean = true
    }

    /// true — сработало двойное нажатие.
    public mutating func keyUp(at t: TimeInterval) -> Bool {
        guard downClean else { return false }
        downClean = false
        if firstTapUp != nil {
            firstTapUp = nil
            return true
        }
        firstTapUp = t
        return false
    }

    /// Любая другая клавиша, другой модификатор, клик.
    public mutating func otherInput() {
        downClean = false
        firstTapUp = nil
    }
}
