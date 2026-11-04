import Testing
@testable import ValloCore

struct SwitcherRulesTests {
    @Test func rulesLoadFromData() {
        let rules = SwitcherRules.shared
        #expect(rules.shortWords[.ru]!.contains("ок"))
        #expect(rules.abbreviations[.en]!.contains("pdf"))
        #expect(rules.misspellings["вобщем"] == "в общем")
        #expect(rules.unitsAfterNumber[.ru]!.contains("руб"))
        #expect(rules.extraRussianWords.contains("сожжен"))
        #expect(rules.unitsBeforeUnit == ["тыс", "млн", "млрд"])
    }
}
