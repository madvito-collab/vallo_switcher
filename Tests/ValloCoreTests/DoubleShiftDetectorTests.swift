import Testing
@testable import ValloCore

@Suite struct DoubleShiftDetectorTests {
    @Test func twoQuickTapsFire() {
        var d = DoubleShiftDetector()
        d.leftShiftDown(at: 0)
        let first = d.leftShiftUp(at: 0.05)
        #expect(!first)
        d.leftShiftDown(at: 0.2)
        let second = d.leftShiftUp(at: 0.25)
        #expect(second)
    }

    @Test func slowSecondTapDoesNotFire() {
        var d = DoubleShiftDetector()
        d.leftShiftDown(at: 0)
        _ = d.leftShiftUp(at: 0.05)
        d.leftShiftDown(at: 0.45)
        let fired = d.leftShiftUp(at: 0.5)
        #expect(!fired)
    }

    @Test func shiftWithLetterIsNotTap() {
        var d = DoubleShiftDetector()
        d.leftShiftDown(at: 0)
        _ = d.leftShiftUp(at: 0.05)
        d.leftShiftDown(at: 0.1)
        d.otherInput()                 // Shift+буква — заглавная
        let fired = d.leftShiftUp(at: 0.2)
        #expect(!fired)
    }

    @Test func keyBetweenTapsCancels() {
        var d = DoubleShiftDetector()
        d.leftShiftDown(at: 0)
        _ = d.leftShiftUp(at: 0.05)
        d.otherInput()
        d.leftShiftDown(at: 0.1)
        let fired = d.leftShiftUp(at: 0.15)
        #expect(!fired)
    }

    @Test func threeTapsFireOnce() {
        var d = DoubleShiftDetector()
        var fired = 0
        for i in 0..<3 {
            let t = Double(i) * 0.1
            d.leftShiftDown(at: t)
            if d.leftShiftUp(at: t + 0.03) { fired += 1 }
        }
        #expect(fired == 1)
    }
}
