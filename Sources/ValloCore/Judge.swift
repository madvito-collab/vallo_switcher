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

    /// Частые слова из 1–2 букв: их исправляем на конце слова (решение ученика 2026-10-29).
    /// Остальные короткие слишком легко спутать — только двойным Shift.
    public static let shortWords: [Language: Set<String>] = [
        .ru: ["ок", "да", "не", "ну", "ты", "мы", "вы", "он", "на", "по", "за", "до", "то",
              "я", "и", "в", "с", "а", "о", "у", "к"],
        .en: ["ok", "hi", "is", "it", "to", "in", "on", "at", "of", "no", "so", "we", "me", "my",
              "do", "go", "up", "be", "a", "i"],
    ]

    /// Разговорные словечки: не ошибка раскладки, даже если их нет в словаре (решение ученика 2026-10-30:
    /// «ща» превращалось в «of»).
    public static let slang: [Language: Set<String>] = [
        .ru: ["ща", "щас", "чо", "че", "чё", "ок", "окей", "ага", "угу", "неа", "спс", "пжлст", "пж", "плз", "хз",
              "мб", "кст", "имхо", "лол", "кек", "тыщ", "тож", "щя", "збс", "норм", "оч", "прив", "пасиб", "нзч"],
        .en: ["ok", "lol", "omg", "btw", "imo", "tbh", "thx", "pls", "plz", "idk", "brb", "np", "ty"],
    ]

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
        if learned.contains(Lexicon.normalize(cur)) { return .keep }
        if Judge.isSlang(cur, currentLanguage) { return .keep }
        if cur.count < Judge.minLetters {
            let short = Judge.shortWords
            guard short[currentLanguage.other]!.contains(alt.lowercased()),
                  !short[currentLanguage]!.contains(cur.lowercased()) else { return .keep }
            return .switchLanguage(reason: "«\(alt)» — частое короткое слово \(currentLanguage.other.rawValue.uppercased())")
        }
        let L = lexicon(currentLanguage), A = lexicon(currentLanguage.other)
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

    /// Частое короткое слово или словечко этого языка — кандидат, если контекст укажет на этот язык.
    public static func isShortCandidate(_ word: String, _ language: Language) -> Bool {
        let w = core(word).lowercased()
        return !w.isEmpty && w.count < minLetters && (shortWords[language]!.contains(w) || slang[language]!.contains(w))
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
    static let domains = ["com", "ru", "org", "net", "io", "dev", "app", "me", "co", "ua", "by", "kz", "info"]

    public static func isSpecial(_ raw: String) -> Bool {
        let s = raw.lowercased()
        if s.contains(where: \.isNumber) { return true }
        if s.contains("@") || s.contains("://") || s.contains("www.") { return true }
        return domains.contains { s.hasSuffix("." + $0) }
    }
}
