public struct KeyEvent: Sendable {
    public let stroke: KeyStroke
    public let hasCommandControlOrOption: Bool

    public init(stroke: KeyStroke, hasCommandControlOrOption: Bool) {
        self.stroke = stroke
        self.hasCommandControlOrOption = hasCommandControlOrOption
    }
}

/// Что сделать «рукам» приложения.
public enum Action: Equatable, Sendable {
    case delete(Int)
    case insert(String)
    case switchLayout(Language)
    case replaceSelection(String)
    case learn(String)
}

public enum EngineOutput: Equatable, Sendable {
    /// Нажатие уходит в приложение как есть.
    case passThrough
    /// Нажатие проглатываем и делаем действия (они уже включают проглоченную клавишу).
    case consume([Action])
}

/// Движок: память набора + решения судьи → действия. Без системных API.
public final class Engine {
    public static let bufferLimit = 200
    static let resetKeys: Set<UInt16> = [36, 48, 53, 76, 123, 124, 125, 126, 115, 119, 116, 121, 117]
    static let backspaceKey: UInt16 = 51

    public var autoSwitchEnabled = true
    public var judge: Judge
    public private(set) var currentLanguage: Language
    public private(set) var lastDecision: String?

    private let layouts: LayoutPair

    /// Нажатие и раскладка, в которой оно сейчас показано на экране.
    private struct Entry {
        let stroke: KeyStroke
        var language: Language
    }

    private var buffer: [Entry] = []
    /// Начало текущего слова в буфере.
    private var wordStart = 0
    /// Текущее слово автоисправлено (из какого языка).
    private var autoSwitchedFrom: Language?
    /// В текущем слове автопереключение больше не трогаем.
    private var wordLocked = false

    /// Отменяемое автоисправление: слово с `start` и всё после него (граница), из языка `from`.
    private struct Undo {
        let start: Int
        let from: Language
    }
    private var undo: Undo?

    /// Двойной Shift сейчас откатит автоисправление (тогда выделение не читаем).
    public var canUndo: Bool { undo != nil }

    public init(layouts: LayoutPair, judge: Judge, currentLanguage: Language) {
        self.layouts = layouts
        self.judge = judge
        self.currentLanguage = currentLanguage
    }

    // MARK: - Нажатия

    public func handleKey(_ e: KeyEvent) -> EngineOutput {
        let stroke = e.stroke
        if e.hasCommandControlOrOption || Engine.resetKeys.contains(stroke.keyCode) {
            reset()
            return .passThrough
        }
        if stroke.keyCode == Engine.backspaceKey {
            handleBackspace()
            return .passThrough
        }
        if layouts.en.character(for: stroke) == nil && layouts.ru.character(for: stroke) == nil {
            reset()
            return .passThrough
        }
        if buffer.count >= Engine.bufferLimit { reset() }
        return isBoundary(stroke) ? handleBoundary(stroke) : handleLetter(stroke)
    }

    private func handleLetter(_ stroke: KeyStroke) -> EngineOutput {
        // первая буква нового слова — откат прошлого слова больше не доступен
        if wordStart == buffer.count { undo = nil }
        let previous = display(wordStart..<buffer.count).count
        buffer.append(Entry(stroke: stroke, language: currentLanguage))

        guard autoSwitchEnabled, !wordLocked else { return .passThrough }
        let word = buffer[wordStart...].map(\.stroke)

        if let from = autoSwitchedFrom {
            let original = Converter.render(word, in: layouts.layout(from))
            let converted = Converter.render(word, in: layouts.layout(currentLanguage))
            guard judge.shouldRevert(original: original, converted: converted, originalLanguage: from) else {
                return .passThrough
            }
            lastDecision = "«\(converted)» не похоже на слово — вернул «\(original)»"
            setWordLanguage(from)
            wordLocked = true
            undo = nil
            return .consume([.delete(previous), .insert(original), .switchLayout(from)])
        }

        let current = Converter.render(word, in: layouts.layout(currentLanguage))
        let alternative = Converter.render(word, in: layouts.layout(currentLanguage.other))
        guard case .switchLanguage(let reason) = judge.midWord(current: current, alternative: alternative,
                                                               currentLanguage: currentLanguage) else {
            return .passThrough
        }
        lastDecision = reason
        let from = currentLanguage
        autoSwitchedFrom = from
        undo = Undo(start: wordStart, from: from)
        setWordLanguage(from.other)
        return .consume([.delete(previous), .insert(alternative), .switchLayout(from.other)])
    }

    private func handleBoundary(_ stroke: KeyStroke) -> EngineOutput {
        defer { startNewWord() }
        let word = buffer[wordStart...].map(\.stroke)
        guard autoSwitchEnabled, !wordLocked, autoSwitchedFrom == nil, !word.isEmpty else {
            buffer.append(Entry(stroke: stroke, language: currentLanguage))
            return .passThrough
        }
        let current = Converter.render(word, in: layouts.layout(currentLanguage))
        let alternative = Converter.render(word, in: layouts.layout(currentLanguage.other))
        guard case .switchLanguage(let reason) = judge.wordEnd(current: current, alternative: alternative,
                                                               currentLanguage: currentLanguage) else {
            buffer.append(Entry(stroke: stroke, language: currentLanguage))
            return .passThrough
        }
        lastDecision = reason
        let from = currentLanguage
        undo = Undo(start: wordStart, from: from)
        setWordLanguage(from.other)
        buffer.append(Entry(stroke: stroke, language: currentLanguage))
        let inserted = alternative + (layouts.layout(currentLanguage).character(for: stroke) ?? "")
        return .consume([.delete(current.count), .insert(inserted), .switchLayout(currentLanguage)])
    }

    private func handleBackspace() {
        guard !buffer.isEmpty else { return }
        buffer.removeLast()
        undo = nil
        if buffer.count < wordStart {
            // стёрли границу и залезли в прошлое слово — его больше не трогаем
            wordStart = (buffer.lastIndex { isBoundary($0.stroke) }).map { $0 + 1 } ?? 0
            wordLocked = true
        }
    }

    // MARK: - Двойной Shift

    public func handleDoubleShift(selection: String?) -> [Action] {
        if let u = undo {
            let range = u.start..<buffer.count
            let shown = display(range)
            let original = Converter.render(buffer[range].map(\.stroke), in: layouts.layout(u.from))
            let learned = Lexicon.normalize(Judge.core(original))
            judge.learned.insert(learned)
            for i in range { buffer[i].language = u.from }
            currentLanguage = u.from
            wordLocked = true
            undo = nil
            lastDecision = "откат: «\(shown)» → «\(original)», запомнил «\(learned)»"
            return [.delete(shown.count), .insert(original), .switchLayout(u.from), .learn(learned)]
        }

        if let selection, !selection.isEmpty {
            let cyrillic = selection.filter { Lexicon.isInAlphabet(Character($0.lowercased()), .ru) }.count
            let latin = selection.filter { Lexicon.isInAlphabet(Character($0.lowercased()), .en) }.count
            let from: Language = cyrillic > latin ? .ru : .en
            let converted = Converter.convert(selection, from: layouts.layout(from), to: layouts.layout(from.other))
            reset()
            currentLanguage = from.other
            return [.replaceSelection(converted), .switchLayout(from.other)]
        }

        guard !buffer.isEmpty else { return [] }
        let target = currentLanguage.other
        let shown = display(0..<buffer.count)
        let converted = Converter.render(buffer.map(\.stroke), in: layouts.layout(target))
        for i in buffer.indices { buffer[i].language = target }
        currentLanguage = target
        wordLocked = true
        return [.delete(shown.count), .insert(converted), .switchLayout(target)]
    }

    // MARK: - Сбросы

    /// Клик мышью, смена приложения, навигация.
    public func reset() {
        buffer.removeAll()
        undo = nil
        startNewWord()
    }

    /// Раскладку сменили не мы. Если раскладка та же, что уже стоит, — это эхо нашего же
    /// переключения (Мак присылает уведомление дважды), память набора не трогаем.
    public func externalLayoutChange(to language: Language) {
        guard language != currentLanguage else { return }
        reset()
        currentLanguage = language
    }

    // MARK: - Помощники

    private func startNewWord() {
        wordStart = buffer.count
        autoSwitchedFrom = nil
        wordLocked = false
    }

    private func setWordLanguage(_ language: Language) {
        for i in wordStart..<buffer.count { buffer[i].language = language }
        currentLanguage = language
    }

    /// Как кусок буфера сейчас выглядит на экране.
    private func display(_ range: Range<Int>) -> String {
        buffer[range].compactMap { layouts.layout($0.language).character(for: $0.stroke) }.joined()
    }

    /// Граница слова — клавиша, которая ни в одной раскладке не даёт букву.
    private func isBoundary(_ stroke: KeyStroke) -> Bool {
        let isLetter = { (s: String?) in s?.first?.isLetter ?? false }
        return !isLetter(layouts.en.character(for: stroke)) && !isLetter(layouts.ru.character(for: stroke))
    }
}
