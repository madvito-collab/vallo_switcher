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

    /// Частые аббревиатуры и расширения файлов (список согласован с учеником 2026-10-30). Набранные в чужой
    /// раскладке переводятся («зва» → «pdf»); хоть одна заглавная — пишутся целиком заглавными («Pdf» → «PDF»).
    /// Не берём то, что маленькими — обычное слово (it, us, id, ram, по, ос), и смешанный регистр (iOS, Wi-Fi).
    /// Убраны, потому что в чужой раскладке дают настоящее слово: cv → «см», exe → «учу», рф → «ha», пфр → «gah».
    public static let abbreviations: [Language: Set<String>] = [
        .en: [
            // расширения файлов
            "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "rtf", "csv", "json", "xml", "html", "css", "js",
            "svg", "png", "jpg", "jpeg", "gif", "webp", "heic", "tiff", "psd", "eps", "fig", "mov", "avi", "mkv", "wav",
            "zip", "rar", "dmg", "apk", "iso",
            // аббревиатуры
            "api", "ui", "ux", "kpi", "okr", "hr", "pr", "ceo", "cto", "cfo", "mvp", "qa", "sdk", "url", "usb", "faq",
            "crm", "erp", "seo", "smm", "saas", "nda", "sla", "roi", "gpu", "cpu", "ssd", "vpn", "ai", "ml",
            "llm", "gpt", "ctr", "cpa", "cta", "utm", "qr",
        ],
        .ru: [
            "жкх", "мчс", "мвд", "фсб", "гибдд", "гаи", "снг", "ссср", "сша", "оон", "нато", "сми", "ооо", "оао",
            "зао", "пао", "ип", "инн", "огрн", "кпп", "бик", "снилс", "ндс", "ндфл", "усн", "осаго", "мфц", "фнс",
            "цб", "мрот", "жк", "тц", "тз", "кп", "пк", "смс", "ржд", "мкад", "тэц", "аэс", "гэс", "спг", "вгсч", "чс",
            "нии", "мгу", "мфти", "вшэ", "загс",
        ],
    ]

    /// Сокращения единиц и денег: сразу после числа («850руб», «5 шт») — не ошибка раскладки, хотя «руб» в
    /// английской раскладке — «he,», а «шт» — «in». Без числа переводим как раньше: «in» в русской раскладке частое.
    public static let unitsAfterNumber: [Language: Set<String>] = [
        .ru: ["руб", "коп", "тыс", "млн", "млрд", "долл", "шт", "уп", "экз", "чел", "сек", "мин",
              "кг", "км", "см", "мм", "мл", "кв"],
        .en: [],
    ]

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
    /// иначе тем, кто их часто пишет, придётся вносить каждую в исключения (решение ученика).
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
    static let domains = ["com", "ru", "org", "net", "io", "dev", "app", "me", "co", "ua", "by", "kz", "info"]

    public static func isSpecial(_ raw: String) -> Bool {
        let s = raw.lowercased()
        if s.contains(where: \.isNumber) { return true }
        if s.contains("@") || s.contains("://") || s.contains("www.") { return true }
        return domains.contains { s.hasSuffix("." + $0) }
    }
}
