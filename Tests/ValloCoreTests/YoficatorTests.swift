import Testing
@testable import ValloCore

let yo = try! Yoficator.load(url: repoURL("Data/wordlists/ru-yo.txt"))
/// Исправлятор опечаток, который знает слова из словаря «ё» (как в приложении).
let typosWithYo: TypoCorrector = { var t = typos; t.yoWords = yo; return t }()

@Suite struct YoficatorTests {
    @Test func unambiguousWords() {
        for (typed, fixed) in [("еще", "ещё"), ("ее", "её"), ("далекий", "далёкий"), ("Шмелев", "Шмелёв"), ("всерьез", "всерьёз")] {
            #expect(yo.fix(typed) == fixed, "\(typed)")
        }
    }

    @Test func ambiguousAndDoneUntouched() {
        // двойные (все/всё, Королев/Королёв) не трогаем никогда; уже с «ё» — тоже
        for w in ["все", "узнаем", "небо", "ещё", "привет", "Королев"] { #expect(yo.fix(w) == nil, "\(w)") }
    }

    @Test func keepsCase() {
        #expect(yo.fix("Еще") == "Ещё")
        #expect(yo.fix("ЕЩЕ") == "ЕЩЁ")
        #expect(yo.fix("еЩе") == nil)
    }

    @Test func properNamesOnlyCapitalized() {
        #expect(yo.fix("Елкин") == "Ёлкин")
        #expect(yo.fix("елкин") == nil)
    }

    @Test func phrase() {
        // голосовой ввод вставляет текст целиком — правый Shift по выделенному
        #expect(yo.fixPhrase("Да, еще все верно. Ее нет") == "Да, ещё все верно. Её нет")
    }
}
