import Testing
@testable import ValloCore

@Suite struct EmojiBracketsTests {
    func f(_ s: String, _ set: EmojiBrackets = .millennial) -> String {
        set.apply(Array(s)).map { String($0) } ?? s
    }

    @Test func bracketsAfterWord() {
        #expect(f("ок) ") == "ок🙂 ")
        #expect(f("ок)) ") == "ок😄 ")
        #expect(f("ок))) ") == "ок😂 ")
        #expect(f("ок))))))\n") == "ок😂\n")   // больше, чем в наборе — самый длинный
        #expect(f("жаль( ") == "жаль🙁 ")
        #expect(f("жаль ((( ") == "жаль 😭 ")
    }

    @Test func zoomer() {
        #expect(f("ору))) ", .zoomer) == "ору💀 ")
        #expect(f("ок)) ", .zoomer) == "ок😭 ")
    }

    @Test func onlyOnBoundary() {
        #expect(EmojiBrackets.millennial.apply(Array("ок))")) == nil)
        #expect(EmojiBrackets.millennial.apply(Array("ок)),")) == nil)
    }

    @Test func textBracketsStay() {
        #expect(f("ну (вот так) ") == "ну (вот так) ")
        #expect(f("1) ") == "1) ")
        #expect(f("ну (вот ") == "ну (вот ")
        #expect(f(":) ") == ":) ")
        #expect(f(";( ") == ";( ")
    }

    @Test func authorExample() {
        // «( так я поставил смайлик грусти )»: первая — грусть, тогда последняя уже ничего не закрывает
        let first = f("( ")
        #expect(first == "🙁 ")
        #expect(f(first + "так грусти ) ") == "🙁 так грусти 🙂 ")
    }

    @Test func bracketOnNewLineIsNotClosed() {
        #expect(f("(вот так\nну) ") == "(вот так\nну🙂 ")
    }

    @Test func frozenIsUntouched() {
        #expect(EmojiBrackets.millennial.apply(Array("ок)) "), frozen: 3) == nil)
    }

    @Test func customSet() {
        let mine = EmojiBrackets(pairs: [")": "🫶", "))": "", "x": "🙃", "(": "💩"])
        #expect(mine.pairs.count == 2)            // пустой смайлик и не скобки — выкинуты
        #expect(f("ок)) ", mine) == "ок🫶 ")       // «))» нет — берём «)»
        #expect(f("ну( ", mine) == "ну💩 ")
    }
}
