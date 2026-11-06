import Testing
@testable import ValloCore

/// Телефоны и разряды чисел: мобильные +7 916 123-45-67, городские +7 (812) 555-55-55,
/// 8 800 остаётся с восьмёркой; разряды с 5 цифр, четыре цифры — только с валютой.
@Suite struct NumbersPhonesTests {
    func f(_ s: String) -> String { Typographer.format(s) }

    @Test func mobilePhones() {
        for s in ["89161234567", "+79161234567", "+7 916 1234567", "8 (916) 123 45 67", "+7-916-123-45-67", "8 916 123-45-67"] {
            #expect(f(s) == "+7\(nb)916\(nb)123-45-67", "\(s)")
        }
    }

    @Test func cityPhones() {
        #expect(f("88125555555") == "+7\(nb)(812)\(nb)555-55-55")
        #expect(f("+7 495 123 45 67") == "+7\(nb)(495)\(nb)123-45-67")
        #expect(f("8 (343) 222-33-44") == "+7\(nb)(343)\(nb)222-33-44")
    }

    @Test func tollFree() {
        #expect(f("88005553535") == "8\(nb)800\(nb)555-35-35")
        #expect(f("8 800 555 35 35") == "8\(nb)800\(nb)555-35-35")
    }

    @Test func phoneInText() {
        #expect(f("звоните 89161234567, ждём") == "звоните +7\(nb)916\(nb)123-45-67, ждём")
        // уже оформленный не меняется
        let done = "+7\(nb)916\(nb)123-45-67"
        #expect(f(done) == done)
    }

    @Test func notPhones() {
        #expect(f("79161234567") == "79161234567")          // без + и без 8 — неизвестно что
        #expect(f("891612345678") == "891612345678")        // 12 цифр
        #expect(f("id89161234567") == "id89161234567")
    }

    @Test func groupsFromFiveDigits() {
        #expect(f("340000") == "340\(nb)000")
        #expect(f("зарплата 1234567,89") == "зарплата 1\(nb)234\(nb)567,89")
        #expect(f("10000 человек") == "10\(nb)000\(nb)человек")
        #expect(f("1000000000") == "1\(nb)000\(nb)000\(nb)000")
    }

    @Test func fourDigitsOnlyWithCurrency() {
        #expect(f("5000р") == "5\(nb)000\(nb)₽")
        #expect(f("5000 ₽") == "5\(nb)000\(nb)₽")
        #expect(f("1500 $") == "1\(nb)500\(nb)$")
        #expect(f("$1500") == "$1\(nb)500")
        #expect(f("€2500") == "€2\(nb)500")
        #expect(f("в 2026 году") == "в\(nb)2026\(nb)году")
        #expect(f("1000") == "1000")
    }

    @Test func numbersLeftAlone() {
        for s in ["A12345", "v10000", "№ 12345", "10:00", "site.com/12345", "12345@mail.ru", "012345", "4276123456789012",
                  "версия 1.10000", "12345678901"] {
            let out = f(s)
            #expect(!out.contains("\(nb)000") && !out.contains("\(nb)345") && !out.contains("\(nb)789"), "\(s) → \(out)")
        }
    }

    @Test func liveWaitsForEnd() {
        // на лету — только когда число закончено
        #expect(Typographer.format("10000", final: false) == "10000")
        #expect(Typographer.format("10000 ", final: false) == "10\(nb)000 ")
        #expect(Typographer.format("89161234567", final: false) == "89161234567")
        #expect(Typographer.format("89161234567 ", final: false) == "+7\(nb)916\(nb)123-45-67 ")
    }

    @Test func liveTyping() {
        func screen(_ text: String) -> String {
            var live = LiveTypographer()
            live.reset(knownLineStart: true)
            var out = ""
            for c in text {
                if let e = live.typed(c) { out.removeLast(e.delete); out += e.insert } else { out.append(c) }
            }
            return out
        }
        #expect(screen("зарплата 340000 рублей") == "зарплата 340\(nb)000\(nb)рублей")
        #expect(screen("звони 89161234567 ок") == "звони +7\(nb)916\(nb)123-45-67\(nb)ок")   // число + слово — неразрывный, как везде
        #expect(screen("в 2026 году") == "в\(nb)2026\(nb)году")
        // в начале поля после клика (слева неизвестно что) номер тоже оформляется
        var live = LiveTypographer()
        live.reset()
        var out = ""
        for c in "89161234567 " { if let e = live.typed(c) { out.removeLast(e.delete); out += e.insert } else { out.append(c) } }
        #expect(out == "+7\(nb)916\(nb)123-45-67 ")
    }
}
