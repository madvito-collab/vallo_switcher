import Foundation

/// Ловит двойной левый Shift: два «чистых» нажатия подряд (без других клавиш),
/// второе начинается не позже `window` после отпускания первого.
public struct DoubleShiftDetector: Sendable {
    public static let window: TimeInterval = 0.3

    /// Shift сейчас зажат и пока «чистый».
    private var downClean = false
    /// Когда отпустили первый чистый Shift.
    private var firstTapUp: TimeInterval?

    public init() {}

    public mutating func leftShiftDown(at t: TimeInterval) {
        if let up = firstTapUp, t - up > DoubleShiftDetector.window { firstTapUp = nil }
        downClean = true
    }

    /// true — сработал двойной Shift.
    public mutating func leftShiftUp(at t: TimeInterval) -> Bool {
        guard downClean else { return false }
        downClean = false
        if firstTapUp != nil {
            firstTapUp = nil
            return true
        }
        firstTapUp = t
        return false
    }

    /// Любая другая клавиша, правый Shift, клик.
    public mutating func otherInput() {
        downClean = false
        firstTapUp = nil
    }
}
