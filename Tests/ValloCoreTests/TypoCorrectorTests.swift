import Foundation
import Testing
@testable import ValloCore

let typos = try! TypoCorrector.load(url: repoURL("Data/wordlists/ru-typos.txt"), ignored: [])

/// Детерминированный генератор для синтетических промахов.
struct SeededRandom: RandomNumberGenerator {
    var state: UInt64
    mutating func next() -> UInt64 {
        state = state &* 6364136223846793005 &+ 1442695040888963407
        return state
    }
}

@Suite struct TypoCorrectorTests {
    @Test func fixesCommonTypos() {
        for t in ["првет", "приввет", "привте"] { #expect(typos.correct(t) == "привет", "\(t)") }
        #expect(typos.correct("пожалуста") == "пожалуйста")
        #expect(typos.correct("сегодян") == "сегодня")
        #expect(typos.correct("спасиббо") == "спасибо")
        #expect(typos.correct("нормалньо") == "нормально")
    }

    @Test func missingSoftSignAtEnd() {
        #expect(typos.correct("можеш") == "можешь")
        #expect(typos.correct("будеш") == "будешь")
        #expect(typos.correct("хочеш") == "хочешь")
        #expect(typos.correct("Делаеш") == "Делаешь")
        #expect(typos.correct("можш") == nil)      // два промаха — не трогаем
        #expect(typos.correct("малыш") == nil)     // настоящее слово на «ш»
    }

    @Test func workVocabulary() {
        // рабочие слова — из письменного словаря (Википедия)
        let work = [("навки", "навыки"), ("тарфи", "тариф"), ("проетк", "проект"), ("компаия", "компания"),
                    ("сотрудиник", "сотрудник"), ("зарплта", "зарплата"), ("настрйоки", "настройки"), ("подключеия", "подключения")]
        let fixed = work.filter { typos.correct($0.0) == $0.1 }
        print("Рабочие: \(fixed.count)/\(work.count)")
        #expect(fixed.count >= 6)
    }

    @Test func keepsCase() {
        #expect(typos.correct("Првет") == "Привет")
        #expect(typos.correct("ПРВЕТ") == "ПРИВЕТ")
    }

    @Test func keepsSlangItAndRare() {
        let control = """
        фигма фигму фигме мокап мокапы фреймы задеплоил пушнуть засинкаемся ревью айфон нетфликс скрин скриншот
        пулл реквест коммит закоммитил смержить мерж фича баг багфикс деплой прод дизайнер дизайнеры юзер юзеры
        ща щас чо спс хз норм кек лол имхо телега телеге вотсап
        гугл гуглить загуглил апрув заапрувил дедлайн дедлайны созвон созвонимся митап кофейка кстати короче
        ваще типа капец офигеть прикольно залипать залип тусить зашквар хайп кринж кринжово вайб вайбкодить
        смузи латте бариста кейс кейсы бриф брифинг онбординг лендинг лендинга прототип прототипы фигмы планку уговорю
        """.split(whereSeparator: \.isWhitespace).map(String.init)
        for w in control { #expect(typos.correct(w) == nil, "\(w)") }
    }

    @Test func namesAndBrandsKept() {
        for w in ["Сбербанк", "Виталий", "Фигма", "Яндекс", "Телеграм", "Озон", "Авито", "Пушкин", "Москва", "Шмелев"] {
            #expect(typos.correct(w) == nil, "\(w)")
        }
    }

    @Test func hyphenParticles() {
        #expect(typos.correctPhrase("какой то баг ктото") == "какой-то баг кто-то")
    }

    @Test func pronounToNotJoinedLive() {
        // «что то решение» — «то» здесь местоимение; на лету «что/как/чем» + «то» не склеиваем
        #expect(typos.joinParticles("я думаю что то решение верное", live: true) == "я думаю что то решение верное")
        #expect(typos.joinParticles("какой то баг", live: true) == "какой-то баг")
    }

    @Test func mixedCaseKept() {
        #expect(typos.correct("пРвет") == nil)
        #expect(typos.correct("ПРвет") == nil)
    }

    @Test func ignoredWordsKept() throws {
        let t = try TypoCorrector.load(url: repoURL("Data/wordlists/ru-typos.txt"), ignored: ["приввет"])
        #expect(t.correct("приввет") == nil)
    }

    @Test func syntheticTypoQuality() {
        var rng = SeededRandom(state: 7)
        let alphabet = Array("абвгдежзийклмнопрстуфхцчшщъыьэюя")
        let pool = typos.words(from: 300, to: 6000).filter { $0.count >= 5 }
        var total = 0, right = 0, wrong = 0
        for _ in 0..<150 {
            let w = Array(pool.randomElement(using: &rng)!)
            let i = Int.random(in: 1..<(w.count - 1), using: &rng)
            var t = w
            switch Int.random(in: 0..<4, using: &rng) {
            case 0: t.insert(alphabet.randomElement(using: &rng)!, at: i)
            case 1: t.remove(at: i)
            case 2: t.swapAt(i, i + 1)
            default: t.insert(w[i], at: i)
            }
            let typo = String(t)
            guard !typos.isKnown(typo) else { continue }
            total += 1
            switch typos.correct(typo) {
            case String(w)?: right += 1
            case nil: break
            default: wrong += 1
            }
        }
        print("Промахи: \(total), верно \(right), неверно \(wrong)")
        #expect(Double(right) >= 0.75 * Double(total))
        #expect(Double(wrong) <= 0.02 * Double(total))
    }

    @Test func rareWordsSafety() {
        var rng = SeededRandom(state: 7)
        let rare = typos.words(from: TypoCorrector.candidateLimit, to: TypoCorrector.candidateLimit + 100_000)
            .filter { $0.count >= 5 }
        let sample = (0..<3000).map { _ in rare.randomElement(using: &rng)! }
        let spoiled = sample.filter { typos.correct($0) != nil }
        print("Редкие: испорчено \(spoiled.count) из 3000: \(spoiled.prefix(5))")
        #expect(Double(spoiled.count) <= 0.005 * 3000)
    }

    @Test func fastEnough() {
        let start = Date()
        for _ in 0..<200 { _ = typos.correct("приввет") }
        let perWord = Date().timeIntervalSince(start) / 200 * 1000
        print("correct: \(perWord) мс на слово")
        #expect(perWord < 5)
    }
}
