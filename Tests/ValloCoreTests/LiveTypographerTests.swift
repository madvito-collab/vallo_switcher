import Testing
@testable import ValloCore

@Suite struct LiveTypographerTests {
    /// Набирает символы по одному, возвращает правку на каждый.
    func typeAll(_ text: String, _ live: inout LiveTypographer) -> [TypoEdit?] {
        text.map { live.typed($0) }
    }

    @Test func dashOnSpace() {
        var live = LiveTypographer()
        live.reset(knownLineStart: true)
        let edits = typeAll("раз - ", &live)
        #expect(edits.last == TypoEdit(delete: 2, insert: "\u{00A0}— "))
        #expect(edits.dropLast().allSatisfy { $0 == nil })
    }

    @Test func quoteOpensAndCloses() {
        var live = LiveTypographer()
        live.reset(knownLineStart: true)
        let edits = typeAll("\"да\"", &live)
        #expect(edits.first == TypoEdit(delete: 0, insert: "«"))
        #expect(edits.last == TypoEdit(delete: 0, insert: "»"))
    }

    @Test func nbspAfterPreposition() {
        var live = LiveTypographer()
        live.reset(knownLineStart: true)
        #expect(typeAll("в ", &live).last == TypoEdit(delete: 0, insert: "\u{00A0}"))
    }

    @Test func backspaceUndoes() {
        var live = LiveTypographer()
        live.reset(knownLineStart: true)
        typeAll("раз - ", &live)
        #expect(live.backspace() == TypoEdit(delete: 3, insert: " - "))
        #expect(live.backspace() == nil)
    }

    @Test func backspaceAfterOtherCharDoesNothing() {
        var live = LiveTypographer()
        live.reset(knownLineStart: true)
        typeAll("в д", &live)
        #expect(live.backspace() == nil)
    }

    @Test func quoteAfterResetOpens() {
        var live = LiveTypographer()
        typeAll("\"да", &live)
        live.reset()
        #expect(live.typed("\"") == TypoEdit(delete: 0, insert: "«"))
    }

    @Test func ellipsisChangesLength() {
        var live = LiveTypographer()
        live.reset(knownLineStart: true)
        let edits = typeAll("ну...", &live)
        #expect(edits.last == TypoEdit(delete: 2, insert: "…"))
        #expect(live.lastEditChangedLength)
    }

    @Test func typographyResumesAfterUndo() {
        // отменил неразрывный пробел после «в» — дальше типографика работает как обычно
        var live = LiveTypographer()
        live.reset(knownLineStart: true)
        typeAll("в ", &live)
        _ = live.backspace()
        let edits = typeAll("доме - ", &live)
        #expect(edits.last == TypoEdit(delete: 2, insert: "\u{00A0}— "))
    }

    @Test func unknownLeftSideAfterReset() {
        // после сброса посреди слова (свитчер исправил «реше…») хвост «но» — не союз
        var live = LiveTypographer()
        typeAll("но", &live)
        #expect(live.typed(" ") == nil)
        // а после настоящего пробела — союз
        #expect(typeAll("но ", &live).last == TypoEdit(delete: 0, insert: "\u{00A0}"))
    }

    @Test func undoneTextIsNotReformatted() {
        // после отмены человек хотел именно дефис — следующий символ не должен снова превратить его в тире
        var live = LiveTypographer()
        typeAll("раз - ", &live)
        _ = live.backspace()
        #expect(live.typed("д") == nil)
    }
}
