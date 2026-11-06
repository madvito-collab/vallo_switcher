public enum Verdict: Equatable, Sendable {
    case wait
    case keep
    case switchLanguage(reason: String)
}

/// Судья: решает, набрано ли слово не в той раскладке.
/// `current` — как слово выглядит сейчас, `alternative` — те же клавиши в другой раскладке.
public struct Judge: Sendable {
    /// На сколько триграммы другого языка должны быть «похожее», чтобы переключить незнакомое слово.
    public static let trigramMargin = 2.0
    /// Раньше этой буквы ничего не решаем.
    public static let minLetters = 3

    /// Частые слова из 1–2 букв: их исправляем на конце слова. Список — `Data/switcher-rules.json`.
    public static var shortWords: [Language: Set<String>] { SwitcherRules.shared.shortWords }

    /// Разговорные словечки: не ошибка раскладки, даже если их нет в словаре. Список — `Data/switcher-rules.json`.
    public static var slang: [Language: Set<String>] { SwitcherRules.shared.slang }

    /// Частые аббревиатуры и расширения файлов: «зва» → «pdf», хоть одна заглавная — пишутся целиком заглавными.
    /// Список и причины исключений — `Data/switcher-rules.json` и `Data/wordlists/README.md`.
    public static var abbreviations: [Language: Set<String>] { SwitcherRules.shared.abbreviations }

    /// Сокращения единиц и денег сразу после числа («850руб», «5 шт») — не ошибка раскладки.
    /// Без числа переводим как раньше. Список — `Data/switcher-rules.json`.
    public static var unitsAfterNumber: [Language: Set<String>] { SwitcherRules.shared.unitsAfterNumber }

    public static func isUnitAfterNumber(_ word: String, _ language: Language) -> Bool {
        unitsAfterNumber[language]!.contains(core(word).lowercased())
    }

    public var learned: Set<String>
    private let en: Lexicon
    private let ru: Lexicon

    public init(en: Lexicon, ru: Lexicon, learned: Set<String>) {
        self.en = en
        self.ru = ru
        self.learned = learned
    }

    private func lexicon(_ language: Language) -> Lexicon { language == .en ? en : ru }

    /// Во время набора: переключаем, только если в текущем языке такое начало невозможно,
    /// а в другом нормальное.
    public func midWord(current: String, alternative: String, currentLanguage: Language) -> Verdict {
        let cur = Judge.core(current), alt = Judge.core(alternative)
        if SpecialText.isSpecial(current) || SpecialText.isSpecial(alternative) { return .wait }
        if Judge.isAbbreviation(current) { return .wait }
        if cur.count < Judge.minLetters { return .wait }
        let normalized = Lexicon.normalize(cur)
        if learned.contains(where: { $0.hasPrefix(normalized) }) { return .wait }
        if Judge.isSlang(cur, currentLanguage) { return .wait }
        let L = lexicon(currentLanguage), A = lexicon(currentLanguage.other)
        if !L.isPrefix(cur) && A.isPrefix(alt) {
            return .switchLanguage(reason: "«\(cur)» не начало \(currentLanguage.rawValue.uppercased())-слова, «\(alt)» — начало \(currentLanguage.other.rawValue.uppercased())")
        }
        return .wait
    }

    /// На конце слова: страховка, если по началу решить не удалось.
    public func wordEnd(current: String, alternative: String, currentLanguage: Language) -> Verdict {
        let cur = Judge.core(current), alt = Judge.core(alternative)
        if SpecialText.isSpecial(current) || SpecialText.isSpecial(alternative) { return .keep }
        // заглавными — не переводим, кроме известной аббревиатуры другого языка («ЗВА» → «PDF»)
        if Judge.isAbbreviation(current), !Judge.isKnownAbbreviation(alternative, currentLanguage.other) { return .keep }
        if learned.contains(Lexicon.normalize(cur)) { return .keep }
        if Judge.isSlang(cur, currentLanguage) { return .keep }
        if Judge.isShort(current: cur, alternative: alt) {
            let short = Judge.shortWords
            guard short[currentLanguage.other]!.contains(alt.lowercased())
                    || Judge.isKnownAbbreviation(alt, currentLanguage.other),
                  !short[currentLanguage]!.contains(cur.lowercased()) else { return .keep }
            return .switchLanguage(reason: "«\(alt)» — частое короткое слово \(currentLanguage.other.rawValue.uppercased())")
        }
        let L = lexicon(currentLanguage), A = lexicon(currentLanguage.other)
        // букв мало только здесь («,sk» — «был», «;r[» — «жкх»): решает лишь то, слово ли другой вариант
        if cur.count < Judge.minLetters {
            guard A.isWord(alt) || Judge.isKnownAbbreviation(alt, currentLanguage.other) else { return .keep }
            return .switchLanguage(reason: "«\(alt)» — слово \(currentLanguage.other.rawValue.uppercased())")
        }
        if L.isWord(cur) { return .keep }
        if A.isWord(alt) { return .switchLanguage(reason: "«\(alt)» — слово \(currentLanguage.other.rawValue.uppercased()), «\(cur)» — нет") }
        let margin = A.trigramScore(alt) - L.trigramScore(cur)
        if margin > Judge.trigramMargin {
            return .switchLanguage(reason: "«\(alt)» похоже на \(currentLanguage.other.rawValue.uppercased()) сильнее на \(margin)")
        }
        return .keep
    }

    /// После автопереключения: вернуть назад, если исправленный вариант перестал быть началом слова,
    /// а исходный выглядит правдоподобнее.
    public func shouldRevert(original: String, converted: String, originalLanguage: Language) -> Bool {
        let L = lexicon(originalLanguage), A = lexicon(originalLanguage.other)
        let orig = Judge.core(original), conv = Judge.core(converted)
        return !A.isPrefix(conv) && L.trigramScore(orig) > A.trigramScore(conv)
    }

    /// Короткое слово (1–2 буквы) в обеих раскладках. Буквы считаем в той, где их больше: «б» и «ю» в английской
    /// раскладке — запятая и точка, и «был» (,sk) иначе выглядел бы словом из двух букв.
    public static func isShort(current: String, alternative: String) -> Bool {
        max(core(current).count, core(alternative).count) < minLetters
    }

    /// Частое короткое слово или словечко этого языка — кандидат, если контекст укажет на этот язык.
    public static func isShortCandidate(_ word: String, _ language: Language) -> Bool {
        let w = core(word).lowercased()
        return !w.isEmpty && w.count < minLetters && (shortWords[language]!.contains(w) || slang[language]!.contains(w))
    }

    /// Слово целиком заглавными (от двух букв) — аббревиатура (ВГСЧ, ЖКХ, PDF): её не переводим,
    /// иначе тем, кто их часто пишет, придётся вносить каждую в исключения.
    public static func isAbbreviation(_ word: String) -> Bool {
        let letters = word.filter(\.isLetter)
        return letters.count >= 2 && letters.allSatisfy(\.isUppercase)
    }

    public static func isKnownAbbreviation(_ word: String, _ language: Language) -> Bool {
        abbreviations[language]!.contains(core(word).lowercased())
    }

    static func isSlang(_ word: String, _ language: Language) -> Bool {
        slang[language]!.contains(word.lowercased())
    }

    /// Обрезает небуквенные символы в начале и в конце.
    public static func core(_ s: String) -> String {
        let chars = Array(s)
        guard let first = chars.firstIndex(where: \.isLetter),
              let last = chars.lastIndex(where: \.isLetter) else { return "" }
        return String(chars[first...last])
    }
}

/// Ссылки, почта, телефоны, наборы с цифрами — автопереключение не трогает.
public enum SpecialText {
    /// Домены верхнего уровня: слово, оканчивающееся на «.домен», — ссылка. Список — `Data/switcher-rules.json`.
    public static var domains: [String] { SwitcherRules.shared.domains }

    public static func isSpecial(_ raw: String) -> Bool {
        let s = raw.lowercased()
        if s.contains(where: \.isNumber) { return true }
        if s.contains("@") || s.contains("://") || s.contains("www.") { return true }
        return domains.contains { s.hasSuffix("." + $0) }
    }
}
