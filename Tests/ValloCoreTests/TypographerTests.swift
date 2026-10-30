import Testing
@testable import ValloCore

let nb = "\u{00A0}"

@Suite struct TypographerTests {
    func f(_ s: String) -> String { Typographer.format(s) }

    @Test func dash() {
        #expect(f("Москва - столица") == "Москва\(nb)— столица")
        #expect(f("раз -- два") == "раз\(nb)— два")
        #expect(f("- Привет") == "— Привет")
        #expect(f("кто-то") == "кто-то")
    }

    @Test func minusAndRanges() {
        #expect(f("5 - 3") == "5 − 3")
        #expect(f("было -5") == "было −5")
        #expect(f("1990-2000") == "1990–2000")
        #expect(f("2026-10-30") == "2026-10-30")
        #expect(f("+7-999-123-45-67") == "+7-999-123-45-67")
    }

    @Test func quotesRussian() {
        #expect(f("\"Он сказал \"привет\"\"") == "«Он сказал „привет“»")
    }

    @Test func quotesEnglish() {
        #expect(f("\"Hello\" it's") == "“Hello” it’s")
    }

    @Test func nbspPrepositions() {
        #expect(f("в доме и для нас") == "в\(nb)доме и\(nb)для\(nb)нас")
        #expect(f("пришёл ли он") == "пришёл\(nb)ли он")
    }

    @Test func nbspNumbersInitials() {
        #expect(f("5 кг") == "5\(nb)кг")
        #expect(f("А. С. Пушкин") == "А.\(nb)С.\(nb)Пушкин")
        #expect(f("№ 5") == "№\(nb)5")
    }

    @Test func ellipsisAndSymbols() {
        #expect(f("ну...") == "ну…")
        #expect(f("(c) 2026") == "© 2026")
        #expect(f("+-5") == "±5")
        #expect(f("a -> b") == "a → b")
        #expect(f("1920x1080") == "1920×1080")
        #expect(f("1/2") == "½")
        #expect(f("(tm)") == "™")
        #expect(f("(r)") == "®")
    }

    @Test func multiplication() {
        #expect(f("5 х 3") == "5 × 3")
        #expect(f("5 x 3") == "5 × 3")
        #expect(f("5*3") == "5×3")
        #expect(f("5 * 3") == "5 × 3")
        // на лету «5 х» успевает стать «5⍽х» (число + слово) — умножение всё равно должно сработать
        #expect(Typographer.format("5\(nb)х 3", final: false) == "5 × 3")
    }

    @Test func spacesCleanup() {
        #expect(f("привет  ,  как") == "привет, как")
    }

    @Test func englishTextUntouchedByRussianRules() {
        #expect(f("I am a cat") == "I am a cat")
    }

    @Test func doubleHyphenOptionKept() {
        #expect(f("ls --help") == "ls --help")
    }

    @Test func idempotent() {
        let inputs = ["Москва - столица", "раз -- два", "- Привет", "5 - 3", "было -5", "1990-2000",
                      "\"Он сказал \"привет\"\"", "\"Hello\" it's", "в доме и для нас", "пришёл ли он",
                      "5 кг", "А. С. Пушкин", "№ 5", "ну...", "(c) 2026", "a -> b", "1920x1080", "привет  ,  как"]
        for x in inputs {
            #expect(f(f(x)) == f(x), "\(x)")
        }
    }

    @Test func liveWaitsForRightContext() {
        #expect(Typographer.format("5 - ", final: false) == "5 - ")
        #expect(Typographer.format("5 - 3", final: false) == "5 − 3")
        #expect(Typographer.format("5 - а", final: false) == "5\(nb)— а")
        #expect(Typographer.format("10-20", final: false) == "10-20")
        #expect(Typographer.format("10-20 ", final: false) == "10–20 ")
    }
}
