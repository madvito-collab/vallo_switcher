import Testing
@testable import ValloCore

@Suite struct ConverterTests {
    func convert(_ text: String, _ from: Language, _ to: Language, _ kind: String) -> String {
        let pair = fixturePair(kind)
        return Converter.convert(text, from: pair.layout(from), to: pair.layout(to))
    }

    @Test(arguments: ["ansi", "iso"])
    func englishTypedRussian(kind: String) {
        #expect(convert("ghbdtn", .en, .ru, kind) == "привет")
        #expect(convert(",eltn", .en, .ru, kind) == "будет")
        #expect(convert("ghbdtn rfr ltkf", .en, .ru, kind) == "привет как дела")
    }

    @Test(arguments: ["ansi", "iso"])
    func casePreserved(kind: String) {
        #expect(convert("Ghbdtn", .en, .ru, kind) == "Привет")
        #expect(convert("GHBDTN", .en, .ru, kind) == "ПРИВЕТ")
    }

    @Test(arguments: ["ansi", "iso"])
    func russianTypedEnglish(kind: String) {
        #expect(convert("руддщ", .ru, .en, kind) == "hello")
    }

    @Test(arguments: ["ansi", "iso"])
    func yoRoundTrip(kind: String) {
        let en = convert("ёжик", .ru, .en, kind)
        #expect(convert(en, .en, .ru, kind) == "ёжик")
    }

    @Test(arguments: ["ansi", "iso"])
    func unmappableCharKept(kind: String) {
        #expect(convert("№", .en, .ru, kind) == "№")
    }

    @Test(arguments: ["ansi", "iso"])
    func capsLockRendersUppercase(kind: String) {
        let pair = fixturePair(kind)
        let strokes = strokesTyping("ghbdtn", in: pair.layout(.en) as! TableKeyLayout)
            .map { KeyStroke(keyCode: $0.keyCode, shift: false, capsLock: true) }
        #expect(Converter.render(strokes, in: pair.layout(.ru)) == "ПРИВЕТ")
    }

    @Test func strokesForUnknownCharIsNil() {
        let pair = fixturePair("ansi")
        let strokes = Converter.strokes(for: "a№", in: pair.layout(.en))
        #expect(strokes.count == 2)
        #expect(strokes[0] != nil)
        #expect(strokes[1] == nil)
    }
}
