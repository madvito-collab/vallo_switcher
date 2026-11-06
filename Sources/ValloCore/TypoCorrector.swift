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

    /// На лету после этих слов «то» часто местоимение («что то решение», «как то, так и другое») — не склеиваем.
    static let ambiguousStems: Set<String> = ["что", "как", "чем"]
    static let particleRegex = try! NSRegularExpression(
        pattern: "(?i)(?<![\\p{L}])(\\p{L}+) (то|нибудь|либо|таки)(?![\\p{L}])")

    /// Частые ошибки «как слышится» — то, что поиск промахов не ловит (две правки, замена буквы).
    /// Точное совпадение, ключи нормализованы (ё → е). Список — `Data/switcher-rules.json`.
    public static var misspellings: [String: String] { SwitcherRules.shared.misspellings }

    /// «Мои слова» — нормализованы (строчные, ё → е).
    public var ignored: Set<String>
    /// Словарь «ё»: настоящие слова, которых нет в словаре опечаток («введем») — не опечатка.
    /// Работает и при выключенной расстановке «ё».
    public var yoWords: Yoficator?
    /// Современные слова (`ru-modern.txt`: чатах, дедлайны) — нормализованы. Без них «чатах» → «чартах».
    public var modernWords: Set<String> = []
    /// Все формы русских слов (OpenCorpora, `ru-forms.txt`): правильное слово не трогаем, даже если оно редкое.
    /// Без него «иконка» → «икона», «исправляется» → «справляется» (проверка на сообщениях автора, 2026-11-06).
    /// Слова, которые бывают только именами, записаны со «*»: их не трогаем, но опечатку в них не превращаем.
    public var forms: SortedWordFile?

    /// Настоящее слово — нарицательное или имя.
    private func isForm(_ w: String) -> Bool {
        guard let forms else { return false }
        return forms.contains(w) || forms.contains(w + "*")
    }

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
        // смешанный регистр («пРвет») — не трогаем: регистр не меняем никогда
        let letters = word.filter(\.isLetter)
        let mixedCase = letters.dropFirst().contains(where: \.isUppercase) && !letters.allSatisfy(\.isUppercase)
        if mixedCase { return nil }
        if let fixed = TypoCorrector.misspellings[w] {
            return ignored.contains(w) ? nil : Self.withCase(of: word, fixed)
        }
        // забытый «ь» в конце: «можеш» → «можешь» (решение автора) — узкое исключение из «окончания не трогаем»
        if w.count >= 4, w.hasSuffix("ш"), w.allSatisfy({ Lexicon.isInAlphabet($0, .ru) }),
           frequency[w] == nil, !ignored.contains(w), !isForm(w), isKnown(w + "ь") {
            return Self.withCase(of: word, (display[w + "ь"] ?? w + "ь"))
        }
        guard w.count >= TypoCorrector.minLength,
              w.allSatisfy({ Lexicon.isInAlphabet($0, .ru) }),
              !isKnown(w), !ignored.contains(w), !Judge.isSlang(w, .ru), yoWords?.contains(w) != true,
              !modernWords.contains(w), !isForm(w) else { return nil }
        if let hyphenated = hyphenate(w) { return Self.withCase(of: word, hyphenated) }

        let own = frequency[w] ?? 0
        // со словарём форм правильное слово сюда не дойдёт — можно смелее: и окончания, и гласные, и редкие слова
        let full = forms != nil
        let candidates = (full ? fullEdits(Array(w)) : edits(Array(w)))
            // со словарём форм — только настоящие нарицательные слова: не «силла», не «тамбов»
            .filter { $0.count >= TypoCorrector.minLength && (full ? frequency[$0] != nil && (forms!.contains($0) || modernWords.contains($0)) : isKnown($0)) }
            // по первой букве почти не промахиваются, а жаргон так портится: «ассеты» → «кассеты»
            .filter { $0.first == w.first }
            .sorted { frequency[$0]! > frequency[$1]! }
        guard let best = candidates.first, let bestFreq = frequency[best],
              bestFreq >= TypoCorrector.minCandidateFrequency else { return nil }
        if candidates.count > 1, bestFreq < TypoCorrector.confidenceRatio * frequency[candidates[1]]! { return nil }
        // редкое, но настоящее слово трогаем, только если рядом в сотни раз более частое
        if own > 0, bestFreq < TypoCorrector.rareWordRatio * own { return nil }
        return Self.withCase(of: word, display[best] ?? best)
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
    /// `live` — на лету: «что/как/чем то» не склеиваем, там «то» часто местоимение.
    public func joinParticles(_ text: String, live: Bool = false) -> String {
        var result = text
        for m in TypoCorrector.particleRegex.matches(in: text, range: NSRange(text.startIndex..., in: text)).reversed() {
            guard let stemRange = Range(m.range(at: 1), in: text), let whole = Range(m.range, in: result) else { continue }
            let stem = Lexicon.normalize(String(text[stemRange]))
            guard TypoCorrector.particleStems.contains(stem),
                  !(live && TypoCorrector.ambiguousStems.contains(stem)) else { continue }
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

    /// Гласные, которые путают на слух: «большенстве» → «большинстве», «содержить» → «содержать».
    static let vowelSwaps: [Character: [Character]] = [
        "е": ["и", "я", "э"], "и": ["е", "ы"], "а": ["о", "я"], "о": ["а"], "я": ["е", "а"], "ы": ["и"], "э": ["е"],
    ]

    /// С какой длины слова пробуем соседнюю клавишу (проверка на сообщениях автора, 2026-11-06).
    static let neighborMinLength = 7

    /// Ряды клавиатуры ЙЦУКЕН — соседние клавиши для «толстого пальца».
    static let rows: [[Character]] = [Array("йцукенгшщзхъ"), Array("фывапролджэ"), Array("ячсмитьбю")]
    static let neighbors: [Character: Set<Character>] = {
        var n: [Character: Set<Character>] = [:]
        func link(_ a: Character, _ b: Character) { n[a, default: []].insert(b); n[b, default: []].insert(a) }
        for (r, row) in rows.enumerated() {
            for (i, ch) in row.enumerated() {
                if i + 1 < row.count { link(ch, row[i + 1]) }
                // ряд ниже сдвинут вправо: клавиша соседствует с двумя над собой
                if r > 0 {
                    let up = rows[r - 1]
                    if i < up.count { link(ch, up[i]) }
                    if i + 1 < up.count { link(ch, up[i + 1]) }
                }
            }
        }
        return n
    }()

    /// Со словарём форм: промахи в любом месте слова, соседняя клавиша, путаница гласных. Конец слова — осторожно, там грамматика:
    /// последнюю букву убираем, только если это соседняя клавиша или повтор («самыен», «невероятныхз»),
    /// дописываем в конец только «ь» («тепер»), гласные в двух последних буквах не меняем («тестим» ≠ «тестем»).
    private func fullEdits(_ w: [Character]) -> Set<String> {
        var out = Set<String>()
        let n = w.count
        for i in 0..<n {
            let last = i == n - 1
            if !last || (n > 1 && (w[i] == w[i - 1] || TypoCorrector.neighbors[w[i - 1]]?.contains(w[i]) == true)) {
                var del = w; del.remove(at: i); out.insert(String(del))
            }
            if i < n - 1 { var swap = w; swap.swapAt(i, i + 1); out.insert(String(swap)) }
            if i < n - 2 {
                for ch in TypoCorrector.vowelSwaps[w[i]] ?? [] { var sub = w; sub[i] = ch; out.insert(String(sub)) }
            }
            // промах по соседней клавише: «ошидкой» → «ошибкой» (д и б рядом); первую букву не трогаем.
            // Только в длинных словах: короткий сленг иначе портится («короч» → «коров», «могет» → «монет»)
            if i > 0 && n >= TypoCorrector.neighborMinLength {
                for ch in TypoCorrector.neighbors[w[i]] ?? [] { var sub = w; sub[i] = ch; out.insert(String(sub)) }
            }
        }
        for i in 0..<n {
            for ch in TypoCorrector.alphabet { var ins = w; ins.insert(ch, at: i); out.insert(String(ins)) }
        }
        out.insert(String(w) + "ь")
        out.remove(String(w))
        return out
    }

    /// Регистр как у набранного: «Првет» → «Привет», «ПРВЕТ» → «ПРИВЕТ». Общий с `Yoficator`.
    static func withCase(of original: String, _ fixed: String) -> String {
        let letters = original.filter(\.isLetter)
        if letters.count > 1, letters.allSatisfy(\.isUppercase) { return fixed.uppercased() }
        if original.first?.isUppercase == true { return fixed.prefix(1).uppercased() + fixed.dropFirst() }
        return fixed
    }
}
