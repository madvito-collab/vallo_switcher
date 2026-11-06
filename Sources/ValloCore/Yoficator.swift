import Foundation

/// Расстановка «ё»: только однозначные слова («еще» → «ещё», «Шмелев» → «Шмелёв»).
/// Двойные (все/всё) в словаре нет — сомневаешься, не ставь. «ё» → «е» не делаем никогда.
/// Словарь — `Data/wordlists/ru-yo.txt` из eyo-kernel (MIT), см. `scripts/build-yo-dictionary.py`.
public struct Yoficator: Sendable {
    /// Слово через «е» (нормализованное) → как писать с «ё» и имя ли это собственное.
    private let words: [String: (spelling: String, proper: Bool)]

    /// Формы с «ё», как в словаре: с заглавной — имя собственное (ставим, только если набрано с заглавной).
    public init(words list: [String]) {
        var words: [String: (spelling: String, proper: Bool)] = [:]
        for raw in list where !raw.isEmpty {
            words[Lexicon.normalize(raw)] = (raw.lowercased(), raw.first!.isUppercase)
        }
        self.words = words
    }

    public static func load(url: URL) throws -> Yoficator {
        let text = try String(contentsOf: url, encoding: .utf8)
        return Yoficator(words: text.split(separator: "\n").map(String.init))
    }

    /// Слово есть в словаре (через «е» или «ё», любой регистр).
    public func contains(_ word: String) -> Bool { words[Lexicon.normalize(word)] != nil }

    /// Слово с «ё» или nil — не трогать.
    public func fix(_ word: String) -> String? {
        guard !word.contains(where: { $0 == "ё" || $0 == "Ё" }),
              let entry = words[Lexicon.normalize(word)] else { return nil }
        let letters = word.filter(\.isLetter)
        // смешанный регистр («еЩе») — не трогаем, как у опечаток
        if letters.dropFirst().contains(where: \.isUppercase), !letters.allSatisfy(\.isUppercase) { return nil }
        if entry.proper, word.first?.isUppercase != true { return nil }
        return TypoCorrector.withCase(of: word, entry.spelling)
    }

    /// Все слова текста (правый Shift по выделенному).
    public func fixPhrase(_ text: String) -> String {
        var out = ""
        var word = ""
        func flush() {
            if !word.isEmpty { out += fix(word) ?? word }
            word = ""
        }
        for ch in text {
            if ch.isLetter { word.append(ch) } else { flush(); out.append(ch) }
        }
        flush()
        return out
    }
}
