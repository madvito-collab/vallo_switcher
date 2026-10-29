import Foundation
import Testing
@testable import ValloCore

let realEN = try! Lexicon.load(url: repoURL("Data/wordlists/en.txt"), language: .en)
let realRU = try! Lexicon.load(url: repoURL("Data/wordlists/ru.txt"), language: .ru)

@Suite struct LexiconTests {
    @Test func prefixesAndWords() {
        let lex = Lexicon(words: ["привет", "при", "hello"])
        #expect(lex.isPrefix("пр"))
        #expect(lex.isPrefix("при"))
        #expect(!lex.isPrefix("прф"))
        #expect(lex.isWord("при"))
        #expect(!lex.isWord("прив"))
    }

    @Test func yoNormalized() {
        let lex = Lexicon(words: ["еж"])
        #expect(lex.isWord("ёж"))
        #expect(lex.isWord("ЁЖ"))
    }

    @Test func realLists() {
        #expect(realRU.isWord("привет"))
        #expect(realRU.isWord("будет"))
        #expect(realEN.isWord("hello"))
        #expect(!realEN.isPrefix("ghb"))
        #expect(realRU.isPrefix("при"))
        #expect(realRU.trigramScore("привет") > realRU.trigramScore("ghbdtn"))
        #expect(realEN.trigramScore("hello") > realEN.trigramScore("руддщ"))
        #expect(!realRU.isWord("the"))   // английское из русского списка отфильтровано
    }

    @Test func measureLoad() throws {
        let start = Date()
        _ = try Lexicon.load(url: repoURL("Data/wordlists/en.txt"), language: .en)
        _ = try Lexicon.load(url: repoURL("Data/wordlists/ru.txt"), language: .ru)
        let elapsed = Date().timeIntervalSince(start)
        print("Загрузка словарей: \(elapsed) с")
        #expect(elapsed < 3)   // в debug-сборке; в release заметно быстрее
    }
}
