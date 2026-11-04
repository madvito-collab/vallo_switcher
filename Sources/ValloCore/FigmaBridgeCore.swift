import Foundation

// Связь свитчера с плагином Фигмы: сообщения, точечные правки текста, статистика исправлений.
// Протокол описан в figma-plugin/PROTOCOL.md — он общий для Мака и будущей винды.

public struct FigmaText: Codable, Equatable, Sendable {
    public let id: String
    public let text: String
    public init(id: String, text: String) { self.id = id; self.text = text }
}

/// Замена куска исходного текста: [start, end) в единицах UTF-16 (как индексы строк в JS).
public struct TextEdit: Codable, Equatable, Sendable {
    public let start: Int
    public let end: Int
    public let insert: String
    public init(start: Int, end: Int, insert: String) { self.start = start; self.end = end; self.insert = insert }
}

public struct FigmaTextEdits: Codable, Equatable, Sendable {
    public let id: String
    public let edits: [TextEdit]
    public init(id: String, edits: [TextEdit]) { self.id = id; self.edits = edits }
}

/// Что исправлено — для окошка плагина.
public struct FixStats: Codable, Equatable, Sendable {
    public var commas = 0, typos = 0, yo = 0, nbsp = 0, dashes = 0, quotes = 0, other = 0
    /// Сколько слоёв изменилось.
    public var layers = 0
    public init() {}

    public var isEmpty: Bool { commas + typos + yo + nbsp + dashes + quotes + other == 0 }

    public mutating func add(_ o: FixStats) {
        commas += o.commas; typos += o.typos; yo += o.yo; nbsp += o.nbsp
        dashes += o.dashes; quotes += o.quotes; other += o.other; layers += o.layers
    }
}

public enum FigmaMode: Equatable, Sendable {
    /// Человек редактирует текст — работать по-старому, с выделенным текстом.
    case text
    /// Ничего не выделено.
    case empty
    case layers([FigmaText])
}

public enum FigmaMessage: Equatable, Sendable {
    // плагин → свитчер
    /// `fresh` — плагин только что запущен (а не переподключился): ему сразу нужна работа.
    case hello(version: Int, file: String, fresh: Bool)
    /// `skipped` — слои, которые нельзя менять (нет шрифта).
    case mode(id: Int, mode: FigmaMode, skipped: Int)
    case done(id: Int)
    // свитчер → плагин
    case press(id: Int)
    case result(id: Int, edits: [FigmaTextEdits], stats: FixStats, skipped: Int)

    private struct Wire: Codable {
        var type: String
        var id: Int?
        var version: Int?
        var file: String?
        var fresh: Bool?
        var mode: String?
        var texts: [FigmaText]?
        var skipped: Int?
        var edits: [FigmaTextEdits]?
        var stats: FixStats?
    }

    public static func decode(_ data: Data) -> FigmaMessage? {
        guard let w = try? JSONDecoder().decode(Wire.self, from: data) else { return nil }
        switch w.type {
        case "hello":
            guard let version = w.version, let file = w.file else { return nil }
            return .hello(version: version, file: file, fresh: w.fresh ?? false)
        case "mode":
            guard let id = w.id else { return nil }
            let skipped = w.skipped ?? 0
            switch w.mode {
            case "text": return .mode(id: id, mode: .text, skipped: skipped)
            case "empty": return .mode(id: id, mode: .empty, skipped: skipped)
            case "layers": return .mode(id: id, mode: .layers(w.texts ?? []), skipped: skipped)
            default: return nil
            }
        case "done":
            return w.id.map { .done(id: $0) }
        case "press":
            return w.id.map { .press(id: $0) }
        case "result":
            guard let id = w.id else { return nil }
            return .result(id: id, edits: w.edits ?? [], stats: w.stats ?? FixStats(), skipped: w.skipped ?? 0)
        default:
            return nil
        }
    }

    public func encode() -> Data {
        var w: Wire
        switch self {
        case let .hello(version, file, fresh): w = Wire(type: "hello", version: version, file: file, fresh: fresh)
        case let .mode(id, mode, skipped):
            w = Wire(type: "mode", id: id, skipped: skipped)
            switch mode {
            case .text: w.mode = "text"
            case .empty: w.mode = "empty"
            case .layers(let texts): w.mode = "layers"; w.texts = texts
            }
        case let .done(id): w = Wire(type: "done", id: id)
        case let .press(id): w = Wire(type: "press", id: id)
        case let .result(id, edits, stats, skipped):
            w = Wire(type: "result", id: id, skipped: skipped, edits: edits, stats: stats)
        }
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        return (try? encoder.encode(w)) ?? Data()
    }
}

// MARK: - Правки

public enum TextDiff {
    /// Больше этого (произведение длин после обрезки общего начала и конца) — одна правка на всю середину,
    /// чтобы не зависнуть на огромном слое.
    static let maxCells = 4_000_000

    /// Точечные правки, превращающие `old` в `new`: по возрастанию, не пересекаются, смещения — в исходном тексте.
    /// Сравнение — по символам Юникода (не режет эмодзи пополам), смещения — в UTF-16.
    public static func edits(from old: String, to new: String) -> [TextEdit] {
        let a = Array(old.unicodeScalars), b = Array(new.unicodeScalars)
        var prefix = 0
        while prefix < a.count, prefix < b.count, a[prefix] == b[prefix] { prefix += 1 }
        var suffix = 0
        while suffix < a.count - prefix, suffix < b.count - prefix,
              a[a.count - 1 - suffix] == b[b.count - 1 - suffix] { suffix += 1 }
        let midA = Array(a[prefix..<(a.count - suffix)]), midB = Array(b[prefix..<(b.count - suffix)])
        if midA.isEmpty && midB.isEmpty { return [] }

        // смещение в UTF-16 для каждого индекса символа старой строки
        var offset = [Int](repeating: 0, count: a.count + 1)
        for i in a.indices { offset[i + 1] = offset[i] + a[i].utf16.count }

        let n = midA.count, m = midB.count
        // длинный слой: таблица не влезает — сравниваем построчно (переносы строк правила не трогают),
        // чтобы правки остались точечными и не слетело оформление
        if n * m > maxCells {
            let linesA = midA.split(separator: "\n", omittingEmptySubsequences: false)
            let linesB = midB.split(separator: "\n", omittingEmptySubsequences: false)
            if linesA.count == linesB.count, linesA.count > 1 {
                var result: [TextEdit] = []
                var base = offset[prefix]
                for (la, lb) in zip(linesA, linesB) {
                    let oldLine = String(String.UnicodeScalarView(la))
                    for e in edits(from: oldLine, to: String(String.UnicodeScalarView(lb))) {
                        result.append(TextEdit(start: base + e.start, end: base + e.end, insert: e.insert))
                    }
                    base += oldLine.utf16.count + 1
                }
                return result
            }
        }

        // пары (i, j) совпавших символов внутри середин
        var matches: [(Int, Int)] = []
        if n > 0, m > 0, n * m <= maxCells {
            // LCS: таблица длин с конца
            let w = m + 1
            var t = [UInt16](repeating: 0, count: (n + 1) * w)
            for i in stride(from: n - 1, through: 0, by: -1) {
                for j in stride(from: m - 1, through: 0, by: -1) {
                    t[i * w + j] = midA[i] == midB[j] ? t[(i + 1) * w + j + 1] + 1
                                                       : max(t[(i + 1) * w + j], t[i * w + j + 1])
                }
            }
            var i = 0, j = 0
            while i < n, j < m {
                if midA[i] == midB[j] { matches.append((i, j)); i += 1; j += 1 }
                else if t[(i + 1) * w + j] >= t[i * w + j + 1] { i += 1 } else { j += 1 }
            }
        }

        // промежутки между совпадениями — правки
        var result: [TextEdit] = []
        var i = 0, j = 0
        for (mi, mj) in matches + [(n, m)] {
            if mi > i || mj > j {
                let insert = String(String.UnicodeScalarView(midB[j..<mj]))
                result.append(TextEdit(start: offset[prefix + i], end: offset[prefix + mi], insert: insert))
            }
            i = mi + 1
            j = mj + 1
        }
        return result
    }
}

// MARK: - Счётчики

/// Сколько исправлено на шаге: сравнение текста до и после шага.
public enum FixCounter {
    static func increase(_ a: String, _ b: String, _ set: Set<Character>) -> Int {
        max(0, b.filter(set.contains).count - a.filter(set.contains).count)
    }

    public static func commas(_ a: String, _ b: String) -> Int { increase(a, b, [","]) }

    public static func yo(_ a: String, _ b: String) -> Int { increase(a, b, ["ё", "Ё"]) }

    /// Сколько слов исправлено.
    public static func typos(_ a: String, _ b: String) -> Int {
        let wa = a.split(whereSeparator: \.isWhitespace), wb = b.split(whereSeparator: \.isWhitespace)
        return wb.difference(from: wa).removals.count
    }

    public static func typography(_ a: String, _ b: String) -> (nbsp: Int, dashes: Int, quotes: Int, other: Int) {
        (increase(a, b, ["\u{00A0}"]),
         increase(a, b, ["—", "–", "−"]),
         increase(a, b, ["«", "»", "„", "“", "”", "‘", "’"]),
         increase(a, b, ["…", "×", "©", "®", "™", "±", "→", "←", "½", "₽"]))
    }
}

// MARK: - Конвейер для слоя

/// Последний шаг обработки слоя: типографика + статистика по всем шагам.
/// Опечатки, «ё» и запятые делаются снаружи (модель живёт в приложении) — сюда приходят их результаты.
public enum FixPipeline {
    public static func finish(original: String, typoFixed: String, yoFixed: String, withCommas: String?) -> (String, FixStats) {
        let commaFixed = withCommas ?? yoFixed
        let result = Typographer.format(commaFixed)
        var stats = FixStats()
        stats.typos = FixCounter.typos(original, typoFixed)
        stats.yo = FixCounter.yo(typoFixed, yoFixed)
        stats.commas = FixCounter.commas(yoFixed, commaFixed)
        let t = FixCounter.typography(commaFixed, result)
        stats.nbsp = t.nbsp; stats.dashes = t.dashes; stats.quotes = t.quotes; stats.other = t.other
        stats.layers = result != original ? 1 : 0
        return (result, stats)
    }
}
