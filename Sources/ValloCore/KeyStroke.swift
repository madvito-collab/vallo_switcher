import Foundation

/// Одно нажатие: физическая клавиша + состояние Shift и Caps Lock.
/// Храним именно нажатия, а не буквы — так одну и ту же клавишу можно
/// «прочитать» в любой раскладке.
public struct KeyStroke: Hashable, Codable, Sendable {
    public let keyCode: UInt16
    public let shift: Bool
    public let capsLock: Bool

    public init(keyCode: UInt16, shift: Bool = false, capsLock: Bool = false) {
        self.keyCode = keyCode
        self.shift = shift
        self.capsLock = capsLock
    }
}

public enum Language: String, Codable, Sendable {
    case en, ru

    public var other: Language { self == .en ? .ru : .en }
}

/// Раскладка: что даёт каждое нажатие.
public protocol KeyLayout: Sendable {
    var language: Language { get }
    func character(for stroke: KeyStroke) -> String?
}

/// Раскладка-таблица. В тестах её грузят из JSON, снятого с системы.
public struct TableKeyLayout: KeyLayout, Codable {
    public let language: Language
    public let table: [KeyStroke: String]

    public init(language: Language, table: [KeyStroke: String]) {
        self.language = language
        self.table = table
    }

    public func character(for stroke: KeyStroke) -> String? { table[stroke] }

    public static func load(url: URL) throws -> TableKeyLayout {
        try JSONDecoder().decode(TableKeyLayout.self, from: Data(contentsOf: url))
    }

    // JSON: {"language":"ru","entries":[{"keyCode":5,"shift":false,"capsLock":false,"char":"п"}, ...]}
    private struct Entry: Codable {
        let keyCode: UInt16
        let shift: Bool
        let capsLock: Bool
        let char: String
    }

    private enum CodingKeys: String, CodingKey { case language, entries }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        language = try c.decode(Language.self, forKey: .language)
        let entries = try c.decode([Entry].self, forKey: .entries)
        var table: [KeyStroke: String] = [:]
        for e in entries {
            table[KeyStroke(keyCode: e.keyCode, shift: e.shift, capsLock: e.capsLock)] = e.char
        }
        self.table = table
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(language, forKey: .language)
        let entries = table
            .map { Entry(keyCode: $0.key.keyCode, shift: $0.key.shift, capsLock: $0.key.capsLock, char: $0.value) }
            .sorted { ($0.keyCode, $0.shift ? 1 : 0, $0.capsLock ? 1 : 0) < ($1.keyCode, $1.shift ? 1 : 0, $1.capsLock ? 1 : 0) }
        try c.encode(entries, forKey: .entries)
    }
}

/// Пара раскладок, между которыми переводим.
public struct LayoutPair: Sendable {
    public let en: any KeyLayout
    public let ru: any KeyLayout

    public init(en: any KeyLayout, ru: any KeyLayout) {
        self.en = en
        self.ru = ru
    }

    public func layout(_ language: Language) -> any KeyLayout {
        language == .en ? en : ru
    }
}
