import Testing
import Foundation
@testable import ValloCore

let modern = try! WordForms.load(url: repoURL("Data/wordlists/ru-modern.txt"))

@Suite struct WordFormsTests {
    @Test func expandsForms() {
        #expect(WordForms.expand("чат(|а|у)") == ["чат", "чата", "чату"])
        #expect(WordForms.expand("фич(а|и|)") == ["фича", "фичи", "фич"])
        #expect(WordForms.expand("эмодзи") == ["эмодзи"])
        #expect(WordForms.expand("  # комментарий") == [])
        #expect(WordForms.expand("сторис # в Инстаграме") == ["сторис"])
    }

    @Test func modernListLoads() {
        #expect(modern.contains("чатах"))
        #expect(modern.contains("дедлайны"))
        #expect(!modern.contains { $0.contains("(") || $0.contains("|") || $0.contains(" ") })
    }

    @Test func typosLeaveModernWords() {
        // «чатах» исправлялось в «чартах» (частое в Википедии): современных слов нет в словаре
        var t = typos
        t.modernWords = Set(modern.map(Lexicon.normalize))
        for w in ["чатах", "чатами", "дедлайны", "созвоне", "скрины", "задеплоить"] {
            #expect(t.correct(w) == nil, "\(w) → \(t.correct(w) ?? "")")
        }
        #expect(t.correct("првет") == "привет")
    }

    @Test func switcherLeavesModernWords() {
        // как с «вобщем»: слово, которого свитчер не знает, он может перевести посреди набора
        let ru = try! Lexicon.load(url: repoURL("Data/wordlists/ru.txt"), language: .ru, extra: modern)
        let judge = Judge(en: realEN, ru: ru, learned: [])
        var failed: [String] = []
        for word in modern {
            let engine = Engine(layouts: ansi, judge: judge, currentLanguage: .ru)
            for stroke in strokesTyping(word + " ", in: ansi.ru as! TableKeyLayout)
            where engine.handleKey(KeyEvent(stroke: stroke, hasCommandControlOrOption: false)) != .passThrough {
                failed.append(word)
                break
            }
        }
        #expect(failed.isEmpty, "свитчер мешает: \(failed)")
    }
}
