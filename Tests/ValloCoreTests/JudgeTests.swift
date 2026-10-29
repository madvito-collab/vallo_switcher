import Testing
@testable import ValloCore

func isSwitch(_ v: Verdict) -> Bool {
    if case .switchLanguage = v { return true }
    return false
}

let smallJudge = Judge(en: Lexicon(words: ["hello", "help", "the"]),
                       ru: Lexicon(words: ["привет", "при", "будет"]),
                       learned: [])

@Suite struct JudgeTests {
    @Test func waitsBeforeThirdLetter() {
        #expect(smallJudge.midWord(current: "gh", alternative: "пр", currentLanguage: .en) == .wait)
    }

    @Test func switchesOnThirdLetter() {
        #expect(isSwitch(smallJudge.midWord(current: "ghb", alternative: "при", currentLanguage: .en)))
    }

    @Test func waitsWhileValidInCurrent() {
        #expect(smallJudge.midWord(current: "hel", alternative: "рук", currentLanguage: .en) == .wait)
    }

    @Test func commaIsCutInEnglish() {
        // в EN запятая отрезается — остаётся 2 буквы
        #expect(smallJudge.midWord(current: ",el", alternative: "буд", currentLanguage: .en) == .wait)
        #expect(isSwitch(smallJudge.midWord(current: ",elt", alternative: "буде", currentLanguage: .en)))
    }

    @Test func wordEndDecisions() {
        #expect(isSwitch(smallJudge.wordEnd(current: "ghbdtn", alternative: "привет", currentLanguage: .en)))
        #expect(smallJudge.wordEnd(current: "hello", alternative: "руддщ", currentLanguage: .en) == .keep)
        #expect(smallJudge.wordEnd(current: "z", alternative: "я", currentLanguage: .en) == .keep)
    }

    @Test func learnedWordBlocksSwitch() {
        var judge = smallJudge
        judge.learned = ["ghbdtn"]
        #expect(judge.midWord(current: "ghb", alternative: "при", currentLanguage: .en) == .wait)
        #expect(judge.wordEnd(current: "ghbdtn", alternative: "привет", currentLanguage: .en) == .keep)
    }

    @Test func specialText() {
        #expect(SpecialText.isSpecial("me@mail"))
        #expect(SpecialText.isSpecial("site.com"))
        #expect(SpecialText.isSpecial("+79991234567"))
        #expect(SpecialText.isSpecial("https://x"))
        #expect(!SpecialText.isSpecial("k.lb"))   // это «люди»
        #expect(smallJudge.midWord(current: "abc1", alternative: "фис1", currentLanguage: .en) == .wait)
    }

    @Test func revertsWhenConvertedStopsBeingPrefix() {
        // словари сопоставимого размера: на словаре из одного слова сглаживание само завышает оценку
        let judge = Judge(en: Lexicon(words: ["aghbz", "ghbza"]),
                          ru: Lexicon(words: ["при", "дом", "кот", "лес", "сад"]), learned: [])
        #expect(judge.shouldRevert(original: "ghbz", converted: "прия", originalLanguage: .en))
        let judge2 = Judge(en: Lexicon(words: ["ghost", "abz"]), ru: Lexicon(words: ["привет"]), learned: [])
        #expect(!judge2.shouldRevert(original: "ghbd", converted: "прив", originalLanguage: .en))
    }

    @Test func realDictionaries() {
        let judge = Judge(en: realEN, ru: realRU, learned: [])
        #expect(isSwitch(judge.midWord(current: "ghb", alternative: "при", currentLanguage: .en)))
        #expect(judge.midWord(current: "hel", alternative: "руд", currentLanguage: .en) == .wait)
        // «руд» — нормальное русское начало (руда), поэтому «hello» в RU-раскладке ловится на 4-й букве
        #expect(judge.midWord(current: "руд", alternative: "hel", currentLanguage: .ru) == .wait)
        #expect(isSwitch(judge.midWord(current: "рудд", alternative: "hell", currentLanguage: .ru)))
        #expect(judge.wordEnd(current: "world", alternative: "цщкдв", currentLanguage: .en) == .keep)
    }
}
