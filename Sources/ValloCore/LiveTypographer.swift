/// Правка на экране: стереть `delete` символов перед курсором и вставить `insert`.
public struct TypoEdit: Equatable, Sendable {
    public let delete: Int
    public let insert: String

    public init(delete: Int, insert: String) {
        self.delete = delete
        self.insert = insert
    }
}

/// Типографика на лету: помнит последние набранные символы, на каждый новый прогоняет `Typographer.format`
/// (`final: false`) и превращает разницу в правку. Backspace сразу после правки возвращает набранное.
public struct LiveTypographer: Sendable {
    public static let contextLimit = 60

    /// Текст на экране с последнего сброса (последние `contextLimit` символов).
    private var context: [Character] = []
    /// Столько первых символов контекста не трогаем: человек отменил правку — значит, хотел именно так.
    private var frozen = 0
    /// Последняя правка: что показали и что человек набрал на самом деле.
    private var undo: (shown: [Character], typed: [Character])?
    /// Длина текста на экране изменилась (… вместо ...) — свитчеру надо сбросить память.
    public private(set) var lastEditChangedLength = false

    public init() {}

    /// Человек набрал символ. nil — пропустить символ как есть.
    public mutating func typed(_ char: Character, hint: Language = .ru) -> TypoEdit? {
        undo = nil
        lastEditChangedLength = false
        let raw = context + [char]
        let formatted = Array(Typographer.format(String(raw), final: false, hint: hint))
        guard formatted != raw else {
            setContext(raw)
            return nil
        }
        let p = zip(raw, formatted).prefix { $0 == $1 }.count
        guard p >= frozen else {
            setContext(raw)
            return nil
        }
        let edit = TypoEdit(delete: context.count - p, insert: String(formatted[p...]))
        undo = (shown: Array(formatted[p...]), typed: Array(raw[p...]))
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
        lastEditChangedLength = u.shown.count != u.typed.count
        context = Array(context.dropLast(u.shown.count)) + u.typed
        frozen = context.count
        return TypoEdit(delete: u.shown.count, insert: String(u.typed))
    }

    public mutating func reset() {
        context = []
        frozen = 0
        undo = nil
        lastEditChangedLength = false
    }

    private mutating func setContext(_ chars: [Character]) {
        let extra = max(0, chars.count - LiveTypographer.contextLimit)
        context = Array(chars.dropFirst(extra))
        frozen = max(0, frozen - extra)
    }
}
