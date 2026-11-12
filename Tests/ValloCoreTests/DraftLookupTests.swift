import Testing
@testable import ValloCore

@Suite struct DraftLookupTests {
    let source: [Int64] = [10, 11, 12, 13, 14, 15, 2]

    @Test func emptyOutputDraftsSourceStart() {
        #expect(DraftLookup.draft(output: [], source: source, limit: 3) == [10, 11, 12])
    }

    @Test func continuesAfterMatchedTail() {
        #expect(DraftLookup.draft(output: [10, 11, 12], source: source) == [13, 14, 15, 2])
    }

    @Test func skipsInsertedComma() {
        // 99 — запятая, которой во входе нет: черновик продолжается со следующего слова
        #expect(DraftLookup.draft(output: [10, 11, 12, 99], source: source) == [13, 14, 15, 2])
    }

    @Test func singleTokenTailIsEnough() {
        #expect(DraftLookup.draft(output: [77, 13], source: source) == [14, 15, 2])
    }

    @Test func nothingMatchesNoDraft() {
        #expect(DraftLookup.draft(output: [70, 71, 72, 73, 74], source: source).isEmpty)
    }

    @Test func endOfSourceNoDraft() {
        #expect(DraftLookup.draft(output: [14, 15, 2], source: source).isEmpty)
    }
}
