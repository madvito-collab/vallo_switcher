import Foundation

/// Исправлятор опечаток: только промахи пальцев (лишняя, пропущенная, переставленная, задвоенная буква)
/// на словаре частых слов. Сленг, жаргон, редкие правильные слова и «Мои слова» не трогает.
/// Регистр не меняет никогда. Правила и пороги — из эксперимента 2026-10-30 (см. спеку).
public struct TypoCorrector: Sendable {
    /// Кандидаты в исправление — только из первых `candidateLimit` слов.
    public static let candidateLimit = 40_000
    public static let minLength = 5
    /// Частоты в словаре — на миллион слов × 1000 (см. scripts/build-typo-dictionary.py); 500 = 0,5 на миллион.
    static let minCandidateFrequency = 500
    static let confidenceRatio = 10
    static let rareWordRatio = 300
    static let alphabet = Array("абвгдежзийклмнопрстуфхцчшщъыьэюя")

    /// Частицы, которые пишутся через дефис, и слова, после которых они частицы, а не союз «то».
    static let particles = ["то", "нибудь", "либо", "таки"]
    static let particleStems: Set<String> = [
        "кто", "что", "какой", "какая", "какое", "какие", "какого", "какому", "каким", "каком", "какую", "какими", "каких",
        "где", "куда", "когда", "как", "почему", "зачем", "откуда", "сколько", "чей", "чья", "чье", "чьи",
        "кого", "кому", "кем", "ком", "чего", "чему", "чем",
    ]

    /// «Мои слова» — нормализованы (строчные, ё → е).
    public var ignored: Set<String>

    private let order: [String]                 // слова по убыванию частоты
    private let frequency: [String: Int]        // все слова словаря (до 50 000)
    private let display: [String: String]       // как писать слово (с «ё», если в словаре оно с ё)
    private let rankIndex: [String: Int]        // место слова в словаре

    public init(frequencies: [(String, Int)], ignored: Set<String>) {
        var order: [String] = []
        var frequency: [String: Int] = [:]
        var display: [String: String] = [:]
        for (raw, count) in frequencies {
            let w = Lexicon.normalize(raw)
            guard frequency[w] == nil else { continue }
            order.append(w)
            frequency[w] = count
            display[w] = raw.lowercased()
        }
        self.order = order
        self.frequency = frequency
        self.display = display
        self.rankIndex = Dictionary(uniqueKeysWithValues: order.enumerated().map { ($1, $0) })
        self.ignored = Set(ignored.map(Lexicon.normalize))
    }

    /// Формат как у словаря свитчера: «слово количество», самые частые сверху.
    /// Словарь — `Data/wordlists/ru-typos.txt`: разговорные слова (субтитры) + письменные (Википедия), 150 000 слов.
    public static func load(url: URL, ignored: Set<String>, limit: Int = 150_000) throws -> TypoCorrector {
        let text = try String(contentsOf: url, encoding: .utf8)
        var list: [(String, Int)] = []
        list.reserveCapacity(limit)
        for line in text.split(separator: "\n") {
            guard list.count < limit else { break }
            let parts = line.split(separator: " ")
            guard parts.count == 2, let count = Int(parts[1]) else { continue }
            let word = String(parts[0])
            guard word.lowercased().allSatisfy({ Lexicon.isInAlphabet($0, .ru) }) else { continue }
            list.append((word, count))
        }
        return TypoCorrector(frequencies: list, ignored: ignored)
    }

    /// Слово среди частых — точно не опечатка.
    public func isKnown(_ word: String) -> Bool {
        rankIndex[Lexicon.normalize(word)].map { $0 < TypoCorrector.candidateLimit } ?? false
    }

    /// Слова словаря с места `from` по `to` (для тестов).
    func words(from: Int, to: Int) -> [String] {
        Array(order[min(from, order.count)..<min(to, order.count)])
    }

    // MARK: - Исправление

    /// Исправленное слово или nil — не трогать.
    public func correct(_ word: String) -> String? {
        let w = Lexicon.normalize(word)
        // забытый «ь» в конце: «можеш» → «можешь» (решение ученика) — узкое исключение из «окончания не трогаем»
        if w.count >= 4, w.hasSuffix("ш"), w.allSatisfy({ Lexicon.isInAlphabet($0, .ru) }),
           frequency[w] == nil, !ignored.contains(w), isKnown(w + "ь") {
            return withCase(of: word, (display[w + "ь"] ?? w + "ь"))
        }
        guard w.count >= TypoCorrector.minLength,
              w.allSatisfy({ Lexicon.isInAlphabet($0, .ru) }),
              !isKnown(w), !ignored.contains(w), !Judge.isSlang(w, .ru) else { return nil }
        if let hyphenated = hyphenate(w) { return withCase(of: word, hyphenated) }

        let own = frequency[w] ?? 0
        let candidates = edits(Array(w))
            .filter { $0.count >= TypoCorrector.minLength && isKnown($0) }
            .sorted { frequency[$0]! > frequency[$1]! }
        guard let best = candidates.first, let bestFreq = frequency[best],
              bestFreq >= TypoCorrector.minCandidateFrequency else { return nil }
        if candidates.count > 1, bestFreq < TypoCorrector.confidenceRatio * frequency[candidates[1]]! { return nil }
        // редкое, но настоящее слово трогаем, только если рядом в сотни раз более частое
        if own > 0, bestFreq < TypoCorrector.rareWordRatio * own { return nil }
        return withCase(of: word, display[best] ?? best)
    }

    /// Все слова фразы + дефисы частиц («какой то» → «какой-то»).
    public func correctPhrase(_ text: String) -> String {
        var out = ""
        var word = ""
        func flush() {
            if !word.isEmpty { out += correct(word) ?? word }
            word = ""
        }
        for ch in text {
            if ch.isLetter { word.append(ch) } else { flush(); out.append(ch) }
        }
        flush()
        return joinParticles(out)
    }

    /// «какой то» → «какой-то», только после местоимений и наречий (иначе «то» — союз).
    func joinParticles(_ text: String) -> String {
        let pattern = "(?i)(?<![\\p{L}])(\\p{L}+) (то|нибудь|либо|таки)(?![\\p{L}])"
        let regex = try! NSRegularExpression(pattern: pattern)
        var result = text
        for m in regex.matches(in: text, range: NSRange(text.startIndex..., in: text)).reversed() {
            guard let stemRange = Range(m.range(at: 1), in: text), let whole = Range(m.range, in: result),
                  TypoCorrector.particleStems.contains(Lexicon.normalize(String(text[stemRange]))) else { continue }
            result.replaceSubrange(whole, with: String(result[whole]).replacingOccurrences(of: " ", with: "-"))
        }
        return result
    }

    /// «ктото» → «кто-то».
    private func hyphenate(_ w: String) -> String? {
        for p in TypoCorrector.particles where w.hasSuffix(p) {
            let stem = String(w.dropLast(p.count))
            if TypoCorrector.particleStems.contains(stem) { return stem + "-" + p }
        }
        return nil
    }

    /// Варианты на один промах. Последнюю букву не убираем (там окончание: «кейсы» → «кейс» — не опечатка),
    /// пропущенную не вставляем прямо перед последней; переставлять можно и две последние («привте»).
    private func edits(_ w: [Character]) -> Set<String> {
        var out = Set<String>()
        let n = w.count
        for i in 0..<(n - 1) {
            var del = w; del.remove(at: i); out.insert(String(del))
            var swap = w; swap.swapAt(i, i + 1); out.insert(String(swap))
            for ch in TypoCorrector.alphabet {
                var ins = w; ins.insert(ch, at: i); out.insert(String(ins))
            }
        }
        out.remove(String(w))
        return out
    }

    /// Регистр как у набранного: «Првет» → «Привет», «ПРВЕТ» → «ПРИВЕТ».
    private func withCase(of original: String, _ fixed: String) -> String {
        let letters = original.filter(\.isLetter)
        if letters.count > 1, letters.allSatisfy(\.isUppercase) { return fixed.uppercased() }
        if original.first?.isUppercase == true { return fixed.prefix(1).uppercased() + fixed.dropFirst() }
        return fixed
    }
}
