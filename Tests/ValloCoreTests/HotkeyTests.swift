import Foundation
import Testing
@testable import ValloCore

@Suite struct HotkeyTests {
    @Test func twoQuickTapsFire() {
        var d = DoubleTapDetector(key: .leftShift)
        d.keyDown(at: 0)
        let first = d.keyUp(at: 0.05)
        #expect(!first)
        d.keyDown(at: 0.2)
        let second = d.keyUp(at: 0.25)
        #expect(second)
    }

    @Test func slowSecondTapDoesNotFire() {
        var d = DoubleTapDetector(key: .leftShift)
        d.keyDown(at: 0)
        _ = d.keyUp(at: 0.05)
        d.keyDown(at: 0.45)
        let fired = d.keyUp(at: 0.5)
        #expect(!fired)
    }

    @Test func shiftWithLetterIsNotTap() {
        var d = DoubleTapDetector(key: .leftShift)
        d.keyDown(at: 0)
        _ = d.keyUp(at: 0.05)
        d.keyDown(at: 0.1)
        d.otherInput()                 // Shift+буква — заглавная
        let fired = d.keyUp(at: 0.2)
        #expect(!fired)
    }

    @Test func keyBetweenTapsCancels() {
        var d = DoubleTapDetector(key: .leftShift)
        d.keyDown(at: 0)
        _ = d.keyUp(at: 0.05)
        d.otherInput()
        d.keyDown(at: 0.1)
        let fired = d.keyUp(at: 0.15)
        #expect(!fired)
    }

    @Test func threeTapsFireOnce() {
        var d = DoubleTapDetector(key: .leftShift)
        var fired = 0
        for i in 0..<3 {
            let t = Double(i) * 0.1
            d.keyDown(at: t)
            if d.keyUp(at: t + 0.03) { fired += 1 }
        }
        #expect(fired == 1)
    }

    @Test func otherModifierKeyWorks() {
        var d = DoubleTapDetector(key: .rightOption)
        d.keyDown(at: 0)
        _ = d.keyUp(at: 0.05)
        d.keyDown(at: 0.1)
        let fired = d.keyUp(at: 0.15)
        #expect(fired)
    }

    @Test func hotkeyCodableRoundTrip() throws {
        for key in [Hotkey.doubleTap(.leftOption), .combo(keyCode: 17, command: false, option: true, control: true, shift: false)] {
            let data = try JSONEncoder().encode(key)
            #expect(try JSONDecoder().decode(Hotkey.self, from: data) == key)
        }
    }

    @Test func defaultsDiffer() {
        #expect(Hotkey.defaultConvert == .doubleTap(.leftShift))
        #expect(Hotkey.defaultTypography == .doubleTap(.rightShift))
        #expect(Hotkey.defaultConvert != Hotkey.defaultTypography)
    }

    @Test func titles() {
        #expect(Hotkey.combo(keyCode: 17, command: false, option: true, control: true, shift: false).title == "⌃⌥T")
        #expect(Hotkey.doubleTap(.rightShift).title == "Двойной правый Shift")
        #expect(ModifierKey.allCases.map(\.keyCode) == [56, 60, 58, 61, 59])
    }
}
