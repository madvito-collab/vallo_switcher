import Foundation

/// Словарь одного языка: слова, все их начала и статистика сочетаний из трёх букв.
public struct Lexicon: Sendable {
    private let words: Set<String>
    private let prefixes: Set<String>
    private let trigrams: [String: Int]
    private let trigramTotal: Int

    public init(words list: [String]) {
        var words = Set<String>()
        var prefixes = Set<String>()
        var trigrams: [String: Int] = [:]
        var total = 0
        for raw in list {
            let w = Lexicon.normalize(raw)
            guard !w.isEmpty, w.allSatisfy(\.isLetter) else { continue }
            words.insert(w)
            var prefix = ""
            for ch in w {
                prefix.append(ch)
                prefixes.insert(prefix)
            }
            for tri in Lexicon.trigramsOf(w) {
                trigrams[tri, default: 0] += 1
                total += 1
            }
        }
        self.words = words
        self.prefixes = prefixes
        self.trigrams = trigrams
        self.trigramTotal = total
    }

    /// Формат файла: «слово количество» на строку, самые частые сверху.
    /// Берём только слова алфавита языка: в русском списке из субтитров попадается английский, и наоборот.
    /// 20 000 — хвост списков шумный (в EN на 28 477 месте мусорное «ghb»).
    public static func load(url: URL, language: Language, limit: Int = 20_000) throws -> Lexicon {
        let text = try String(contentsOf: url, encoding: .utf8)
        var list: [String] = []
        list.reserveCapacity(limit)
        for line in text.split(separator: "\n") {
            guard list.count < limit else { break }
            guard let word = line.split(separator: " ").first.map(String.init),
                  word.lowercased().allSatisfy({ Lexicon.isInAlphabet($0, language) }) else { continue }
            list.append(word)
        }
        return Lexicon(words: list)
    }

    public func isWord(_ s: String) -> Bool { words.contains(Lexicon.normalize(s)) }

    /// Любое начало любого слова, включая само слово.
    public func isPrefix(_ s: String) -> Bool { prefixes.contains(Lexicon.normalize(s)) }

    /// Насколько строка похожа на слова этого языка: средний log P триграмм
    /// с границами "^^s$", сглаживание add-one. Больше — похожее.
    public func trigramScore(_ s: String) -> Double {
        let tris = Lexicon.trigramsOf(Lexicon.normalize(s))
        guard !tris.isEmpty else { return 0 }
        let denom = Double(trigramTotal + trigrams.count + 1)
        let sum = tris.reduce(0.0) { $0 + log(Double((trigrams[$1] ?? 0) + 1) / denom) }
        return sum / Double(tris.count)
    }

    static func isInAlphabet(_ ch: Character, _ language: Language) -> Bool {
        switch language {
        case .en: return ("a"..."z").contains(ch)
        case .ru: return ("а"..."я").contains(ch) || ch == "ё"
        }
    }

    public static func normalize(_ s: String) -> String {
        s.lowercased().replacingOccurrences(of: "ё", with: "е")
    }

    private static func trigramsOf(_ w: String) -> [String] {
        let chars = Array("^^" + w + "$")
        guard chars.count >= 3 else { return [] }
        return (0...(chars.count - 3)).map { String(chars[$0..<($0 + 3)]) }
    }
}
