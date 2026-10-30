/// Правка на экране: стереть `delete` символов перед курсором и вставить `insert`.
public struct TypoEdit: Equatable, Sendable {
    public let delete: Int
    public let insert: String

    public init(delete: Int, insert: String) {
        self.delete = delete
        self.insert = insert
    }
}

/// Что делать на лету: типографика и/или опечатки.
public struct LiveOptions: Sendable {
    public var typography: Bool
    public var typos: TypoCorrector?

    public init(typography: Bool, typos: TypoCorrector?) {
        self.typography = typography
        self.typos = typos
    }
}

/// Типографика на лету: помнит последние набранные символы, на каждый новый прогоняет `Typographer.format`
/// (`final: false`) и превращает разницу в правку. Backspace сразу после правки возвращает набранное.
public struct LiveTypographer: Sendable {
    public static let contextLimit = 60

    /// Текст на экране с последнего сброса (последние `contextLimit` символов).
    private var context: [Character] = []
    /// Что слева от контекста, неизвестно (сброс посреди текста): правила начала строки и слова не срабатывают.
    private var leftUnknown = true
    /// Контекст начинается посреди слова (свитчер исправил раскладку посреди слова) — первое слово не исправляем.
    private var startsMidWord = false
    /// Столько первых символов контекста не трогаем: человек отменил правку — значит, хотел именно так.
    private var frozen = 0
    /// Последняя правка: что показали и что человек набрал на самом деле.
    private var undo: (shown: [Character], typed: [Character], typoWord: String?)?
    /// После `backspace()`: слово, исправление которого человек отменил (нормализованное) — в «Мои слова».
    public private(set) var undoneTypoWord: String?
    /// Длина текста на экране изменилась (… вместо ...) — свитчеру надо сбросить память.
    public private(set) var lastEditChangedLength = false

    public init() {}

    /// Человек набрал символ. nil — пропустить символ как есть.
    public mutating func typed(_ char: Character, hint: Language = .ru,
                               options: LiveOptions = LiveOptions(typography: true, typos: nil)) -> TypoEdit? {
        undo = nil
        undoneTypoWord = nil
        lastEditChangedLength = false
        let raw = context + [char]
        // сначала опечатка в только что законченном слове, потом типографика
        let (corrected, typoWord) = fixTypo(in: raw, options: options)
        let style = { (chars: [Character], unknown: Bool) in
            options.typography ? LiveTypographer.format(chars, unknownLeft: unknown, hint: hint) : chars
        }
        var formatted = style(corrected, leftUnknown)
        var p = zip(raw, formatted).prefix { $0 == $1 }.count
        if p < frozen {
            // правка задела отменённое место — его не трогаем, а остальное причёсываем как обычно
            formatted = Array(raw[..<frozen]) + style(Array(corrected[frozen...]), true)
            p = zip(raw, formatted).prefix { $0 == $1 }.count
        }
        guard formatted != raw else {
            setContext(raw)
            return nil
        }
        let edit = TypoEdit(delete: context.count - p, insert: String(formatted[p...]))
        undo = (shown: Array(formatted[p...]), typed: Array(raw[p...]), typoWord: typoWord)
        lastEditChangedLength = formatted.count != raw.count
        setContext(formatted)
        return edit
    }

    /// Backspace. Сразу после правки — правка-отмена (вернуть набранное), иначе nil (обычный Backspace).
    public mutating func backspace() -> TypoEdit? {
        guard let u = undo else {
            if !context.isEmpty { context.removeLast() }
            frozen = min(frozen, context.count)
            return nil
        }
        undo = nil
        undoneTypoWord = u.typoWord
        lastEditChangedLength = u.shown.count != u.typed.count
        context = Array(context.dropLast(u.shown.count)) + u.typed
        frozen = context.count
        return TypoEdit(delete: u.shown.count, insert: String(u.typed))
    }

    /// `knownLineStart` — перед контекстом точно начало строки (для тестов и пустого поля).
    /// `midWord` — сброс посреди слова (свитчер исправил раскладку): в памяти дальше будет хвост слова.
    public mutating func reset(knownLineStart: Bool = false, midWord: Bool = false) {
        leftUnknown = !knownLineStart
        startsMidWord = midWord
        context = []
        frozen = 0
        undo = nil
        lastEditChangedLength = false
    }

    private mutating func setContext(_ chars: [Character]) {
        let extra = max(0, chars.count - LiveTypographer.contextLimit)
        context = Array(chars.dropFirst(extra))
        frozen = max(0, frozen - extra)
        if extra > 0 { leftUnknown = true; startsMidWord = true }
    }

    /// Набран символ-граница (не буква) — исправить опечатку в слове перед ним и дефис частицы.
    /// Отменённое место (`frozen`) не трогаем. Возвращает текст и исходное слово, если оно исправлено.
    private func fixTypo(in raw: [Character], options: LiveOptions) -> ([Character], String?) {
        guard let typos = options.typos, let boundary = raw.last, !boundary.isLetter else { return (raw, nil) }
        let body = raw.dropLast()
        let start = body.lastIndex { !$0.isLetter }.map { $0 + 1 } ?? body.startIndex
        guard start >= frozen, start < body.endIndex else { return (raw, nil) }
        // контекст начался посреди слова — это хвост, не трогаем
        guard start > body.startIndex || !startsMidWord else { return (raw, nil) }
        let word = String(body[start...])
        var result = Array(body[..<start])
        var typoWord: String?
        if let fixed = typos.correct(word) {
            result += Array(fixed)
            typoWord = Lexicon.normalize(word)
        } else {
            result += Array(word)
        }
        result.append(boundary)
        let joined = Array(typos.joinParticles(String(result), live: true))
        return (joined.count == result.count ? joined : result, typoWord)
    }

    private static func format(_ chars: [Character], unknownLeft: Bool, hint: Language) -> [Character] {
        let input = unknownLeft ? [Typographer.unknownLeft] + chars : chars
        let out = Array(Typographer.format(String(input), final: false, hint: hint))
        return unknownLeft && out.first == Typographer.unknownLeft ? Array(out.dropFirst()) : out
    }
}
