import Testing
@testable import ValloCore

@Suite struct LayoutFixtureTests {
    @Test(arguments: ["ansi", "iso"])
    func fixturesRenderPrivet(kind: String) {
        let pair = fixturePair(kind)
        let en = pair.layout(.en) as! TableKeyLayout
        let strokes = strokesTyping("ghbdtn", in: en)
        let ru = strokes.compactMap { pair.layout(.ru).character(for: $0) }.joined()
        #expect(ru == "привет")
    }

    @Test(arguments: ["ansi", "iso"])
    func fixturesContainYo(kind: String) {
        let ru = fixturePair(kind).layout(.ru) as! TableKeyLayout
        let yo = ru.table.first { $0.value == "ё" }
        let bigYo = ru.table.first { $0.value == "Ё" }
        #expect(yo != nil)
        #expect(bigYo != nil)
        print("\(kind): ё на keyCode \(yo?.key.keyCode ?? 0)")
    }

    @Test(arguments: ["ansi", "iso"])
    func bracketsAreLetters(kind: String) {
        let pair = fixturePair(kind)
        let en = pair.layout(.en) as! TableKeyLayout
        let strokes = strokesTyping("[];',.", in: en)
        let ru = strokes.compactMap { pair.layout(.ru).character(for: $0) }.joined()
        #expect(ru == "хъжэбю")
    }
}
