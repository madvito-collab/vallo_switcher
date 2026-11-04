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
    /// Нажать клавишу (Enter или Tab, которые мы проглотили, чтобы сначала исправить слово).
    case pressKey(UInt16)
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
    static let resetKeys: Set<UInt16> = [53, 123, 124, 125, 126, 115, 119, 116, 121, 117]
    /// Return, Tab, Enter на цифровой клавиатуре: сначала проверяем слово, потом сбрасываем память.
    static let lineEndKeys: Set<UInt16> = [36, 48, 76]
    public static let backspaceKey: UInt16 = 51

    public var autoSwitchEnabled = true
    public var judge: Judge
    public private(set) var currentLanguage: Language
    public private(set) var lastDecision: String?

    private var layouts: LayoutPair

    /// Нажатие и раскладка, в которой оно сейчас показано на экране.
    private struct Entry {
        var stroke: KeyStroke
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

    /// Короткое слово (1–2 буквы) — самое ненадёжное решение. Если следующее слово ушло в язык `candidate`,
    /// короткое тоже переводим в него. Бывает двух видов: исправленное зря (`ща` → `of`, следом русское)
    /// и оставленное, хотя это было слово другого языка (`of` набрано вместо `ща`, следом русское).
    private struct ShortSwitch {
        let start: Int
        let nextWordStart: Int
        let candidate: Language
    }
    private var shortSwitch: ShortSwitch?

    /// Слова, откаченные один раз. Запоминаем навсегда только со второго отката того же слова —
    /// первый мог быть случайным нажатием. Живёт только в памяти, до перезапуска.
    private var undoneOnce: Set<String> = []

    /// В памяти набора что-то есть. Тогда свежего выделения быть не может: клик, стрелки и Cmd+A сбрасывают буфер.
    public var hasTypedText: Bool { !buffer.isEmpty }

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
        if Engine.lineEndKeys.contains(stroke.keyCode) {
            return handleLineEnd(stroke.keyCode)
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
        // первая буква нового слова: откат прошлого больше не доступен, блокировка прошлого слова снята
        if wordStart == buffer.count {
            undo = nil
            startNewWord()
            if let s = shortSwitch, s.nextWordStart != wordStart { shortSwitch = nil }
        }
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
        if let revert = revertShortSwitch(to: from.other, word: alternative) { return revert }
        undo = Undo(start: wordStart, from: from)
        setWordLanguage(from.other)
        return .consume([.delete(previous), .insert(alternative), .switchLayout(from.other)])
    }

    /// Текущее слово переключаем в `target`, а прошлое короткое было исправлено как раз из `target` —
    /// значит, оно ошибка: стираем оба и печатаем оба в `target`. `word` — текущее слово уже в `target`.
    private func revertShortSwitch(to target: Language, word: String, through end: Int? = nil) -> EngineOutput? {
        guard let s = shortSwitch, s.nextWordStart == wordStart, s.candidate == target else { return nil }
        shortSwitch = nil
        let shownEnd = end ?? buffer.count - 1   // новая буква ещё не на экране
        let shown = display(s.start..<shownEnd)
        let shortPart = Converter.render(buffer[s.start..<wordStart].map(\.stroke), in: layouts.layout(target))
        for i in s.start..<buffer.count { buffer[i].language = target }
        currentLanguage = target
        undo = nil
        lastDecision = "«\(shortPart.trimmingCharacters(in: .whitespaces))» исправил зря — следующее слово тоже \(target.rawValue.uppercased())"
        return .consume([.delete(shown.count), .insert(shortPart + word), .switchLayout(target)])
    }

    private func handleBoundary(_ stroke: KeyStroke) -> EngineOutput {
        defer { startNewWord() }
        let word = buffer[wordStart...].map(\.stroke)
        // слово уже в своей раскладке, но это аббревиатура с заглавной («Pdf») — только поднять регистр
        if autoSwitchEnabled, !wordLocked, !word.isEmpty {
            let range = wordStart..<buffer.count
            let shown = display(range)
            if let upper = capitalizedAbbreviation(range) {
                lastDecision = "«\(shown)» — аббревиатура, пишем заглавными"
                buffer.append(Entry(stroke: stroke, language: currentLanguage))
                let boundaryText = layouts.layout(currentLanguage).character(for: stroke) ?? ""
                return .consume([.delete(shown.count), .insert(upper + boundaryText)])
            }
        }
        guard autoSwitchEnabled, !wordLocked, autoSwitchedFrom == nil, !word.isEmpty else {
            buffer.append(Entry(stroke: stroke, language: currentLanguage))
            return .passThrough
        }
        let current = Converter.render(word, in: layouts.layout(currentLanguage))
        let alternative = Converter.render(word, in: layouts.layout(currentLanguage.other))
        if isUnitAfterNumber(current) {
            buffer.append(Entry(stroke: stroke, language: currentLanguage))
            return .passThrough
        }
        guard case .switchLanguage(let reason) = judge.wordEnd(current: current, alternative: alternative,
                                                               currentLanguage: currentLanguage) else {
            // короткое слово оставили, но его можно прочитать как слово другого языка — решит следующее слово
            let start = wordStart
            buffer.append(Entry(stroke: stroke, language: currentLanguage))
            if Judge.isShortCandidate(alternative, currentLanguage.other) {
                shortSwitch = ShortSwitch(start: start, nextWordStart: buffer.count, candidate: currentLanguage.other)
            }
            return .passThrough
        }
        lastDecision = reason
        let from = currentLanguage
        let boundaryText = layouts.layout(from.other).character(for: stroke) ?? ""
        buffer.append(Entry(stroke: stroke, language: from.other))
        if let revert = revertShortSwitch(to: from.other, word: alternative + boundaryText,
                                          through: buffer.count - 1) {
            return revert
        }
        let isShort = Judge.isShort(current: current, alternative: alternative)
        undo = Undo(start: wordStart, from: from)
        setWordLanguage(from.other)
        if isShort { shortSwitch = ShortSwitch(start: wordStart, nextWordStart: buffer.count, candidate: from) }
        let fixed = capitalizedAbbreviation(wordStart..<buffer.count - 1) ?? alternative
        return .consume([.delete(current.count), .insert(fixed + boundaryText), .switchLayout(currentLanguage)])
    }

    /// Enter и Tab: как граница слова, но после них память набора сбрасывается.
    private func handleLineEnd(_ keyCode: UInt16) -> EngineOutput {
        defer { reset() }
        let word = buffer[wordStart...].map(\.stroke)
        guard autoSwitchEnabled, !wordLocked, autoSwitchedFrom == nil, !word.isEmpty else { return .passThrough }
        let current = Converter.render(word, in: layouts.layout(currentLanguage))
        let alternative = Converter.render(word, in: layouts.layout(currentLanguage.other))
        guard !isUnitAfterNumber(current),
              case .switchLanguage(let reason) = judge.wordEnd(current: current, alternative: alternative,
                                                               currentLanguage: currentLanguage) else {
            return .passThrough
        }
        lastDecision = reason
        currentLanguage = currentLanguage.other
        return .consume([.delete(current.count), .insert(alternative), .switchLayout(currentLanguage), .pressKey(keyCode)])
    }

    private func handleBackspace() {
        guard !buffer.isEmpty else { return }
        buffer.removeLast()
        undo = nil
        shortSwitch = nil
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
            let word = Lexicon.normalize(Judge.core(original))
            for i in range { buffer[i].language = u.from }
            currentLanguage = u.from
            wordLocked = true
            undo = nil
            var actions: [Action] = [.delete(shown.count), .insert(original), .switchLayout(u.from)]
            if undoneOnce.contains(word) {
                judge.learned.insert(word)
                undoneOnce.remove(word)
                actions.append(.learn(word))
                lastDecision = "откат: «\(shown)» → «\(original)», второй раз — запомнил «\(word)»"
            } else {
                undoneOnce.insert(word)
                lastDecision = "откат: «\(shown)» → «\(original)», запомню, если откатишь ещё раз"
            }
            return actions
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
        lastDecision = "двойной Shift: всё набранное → \(target.rawValue.uppercased())"
        return [.delete(shown.count), .insert(converted), .switchLayout(target)]
    }

    // MARK: - Сбросы

    /// Клик мышью, смена приложения, навигация.
    public func reset() {
        buffer.removeAll()
        undo = nil
        shortSwitch = nil
        startNewWord()
    }

    /// Человек выбрал другую раскладку того же языка (например, «Русская — ПК») — новые таблицы.
    public func updateLayouts(_ pair: LayoutPair) {
        layouts = pair
        reset()
    }

    /// Раскладку сменили не мы. Если раскладка та же, что уже стоит, — это эхо нашего же
    /// переключения (Мак присылает уведомление дважды), память набора не трогаем.
    public func externalLayoutChange(to language: Language) {
        guard language != currentLanguage else { return }
        reset()
        currentLanguage = language
    }

    // MARK: - Помощники

    /// Известная аббревиатура хоть с одной заглавной («Pdf», «Жкх») — целиком заглавными (решение автора).
    /// Меняет нажатия в буфере на заглавные и возвращает новый вид слова; иначе nil.
    private func capitalizedAbbreviation(_ range: Range<Int>) -> String? {
        let shown = display(range)
        guard let language = buffer[range].first?.language, Judge.isKnownAbbreviation(shown, language),
              shown.contains(where: \.isUppercase), !Judge.isAbbreviation(shown) else { return nil }
        for i in range { buffer[i].stroke = KeyStroke(keyCode: buffer[i].stroke.keyCode, shift: true) }
        return display(range)
    }

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

    /// «руб», «шт» сразу после числа («850руб», «5 шт», «2 млн руб») — единица, не ошибка раскладки.
    private func isUnitAfterNumber(_ current: String) -> Bool {
        guard Judge.isUnitAfterNumber(current, currentLanguage) else { return false }
        let before = String(display(0..<wordStart).reversed().drop { $0 == " " || $0 == "." })
        if before.first?.isNumber ?? false { return true }
        let previous = String(before.prefix { $0.isLetter }.reversed())
        return ["тыс", "млн", "млрд"].contains(previous.lowercased())
    }

    /// Граница слова — клавиша, которая ни в одной раскладке не даёт букву.
    private func isBoundary(_ stroke: KeyStroke) -> Bool {
        let isLetter = { (s: String?) in s?.first?.isLetter ?? false }
        return !isLetter(layouts.en.character(for: stroke)) && !isLetter(layouts.ru.character(for: stroke))
    }
}
