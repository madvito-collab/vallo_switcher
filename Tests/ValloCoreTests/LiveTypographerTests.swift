import Testing
@testable import ValloCore

@Suite struct LiveTypographerTests {
    /// Набирает символы по одному, возвращает правку на каждый.
    func typeAll(_ text: String, _ live: inout LiveTypographer) -> [TypoEdit?] {
        text.map { live.typed($0) }
    }

    @Test func dashOnSpace() {
        var live = LiveTypographer()
        let edits = typeAll("раз - ", &live)
        #expect(edits.last == TypoEdit(delete: 2, insert: "\u{00A0}— "))
        #expect(edits.dropLast().allSatisfy { $0 == nil })
    }

    @Test func quoteOpensAndCloses() {
        var live = LiveTypographer()
        let edits = typeAll("\"да\"", &live)
        #expect(edits.first == TypoEdit(delete: 0, insert: "«"))
        #expect(edits.last == TypoEdit(delete: 0, insert: "»"))
    }

    @Test func nbspAfterPreposition() {
        var live = LiveTypographer()
        #expect(typeAll("в ", &live).last == TypoEdit(delete: 0, insert: "\u{00A0}"))
    }

    @Test func backspaceUndoes() {
        var live = LiveTypographer()
        typeAll("раз - ", &live)
        #expect(live.backspace() == TypoEdit(delete: 3, insert: " - "))
        #expect(live.backspace() == nil)
    }

    @Test func backspaceAfterOtherCharDoesNothing() {
        var live = LiveTypographer()
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
        let edits = typeAll("ну...", &live)
        #expect(edits.last == TypoEdit(delete: 2, insert: "…"))
        #expect(live.lastEditChangedLength)
    }

    @Test func undoneTextIsNotReformatted() {
        // после отмены человек хотел именно дефис — следующий символ не должен снова превратить его в тире
        var live = LiveTypographer()
        typeAll("раз - ", &live)
        _ = live.backspace()
        #expect(live.typed("д") == nil)
    }
}
