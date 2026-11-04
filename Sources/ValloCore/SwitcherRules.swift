import Foundation

/// Списки правил свитчера (короткие слова, сленг, аббревиатуры, единицы, домены, частые ошибки).
/// Живут в `Data/switcher-rules.json` — общий файл для Мака и Windows; здесь только загрузка.
/// Почему слова в списках, а каких нет — `Data/wordlists/README.md`, раздел «switcher-rules.json».
public struct SwitcherRules: Decodable, Sendable {
    public let shortWords: [Language: Set<String>]
    public let slang: [Language: Set<String>]
    public let abbreviations: [Language: Set<String>]
    public let unitsAfterNumber: [Language: Set<String>]
    public let unitsBeforeUnit: [String]
    public let domains: [String]
    public let extraRussianWords: [String]
    public let misspellings: [String: String]

    private enum CodingKeys: String, CodingKey {
        case shortWords, slang, abbreviations, unitsAfterNumber, unitsBeforeUnit, domains, extraRussianWords, misspellings
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        shortWords = try Self.byLanguage(c.decode([String: Set<String>].self, forKey: .shortWords))
        slang = try Self.byLanguage(c.decode([String: Set<String>].self, forKey: .slang))
        abbreviations = try Self.byLanguage(c.decode([String: Set<String>].self, forKey: .abbreviations))
        unitsAfterNumber = try Self.byLanguage(c.decode([String: Set<String>].self, forKey: .unitsAfterNumber))
        unitsBeforeUnit = try c.decode([String].self, forKey: .unitsBeforeUnit)
        domains = try c.decode([String].self, forKey: .domains)
        extraRussianWords = try c.decode([String].self, forKey: .extraRussianWords)
        misspellings = try c.decode([String: String].self, forKey: .misspellings)
    }

    /// JSON хранит языки строками «ru» / «en»; в коде — `Language`. Отсутствующий язык — пустой набор.
    private static func byLanguage(_ raw: [String: Set<String>]) -> [Language: Set<String>] {
        var result: [Language: Set<String>] = [.en: [], .ru: []]
        for (key, words) in raw {
            if let language = Language(rawValue: key) { result[language] = words }
        }
        return result
    }

    /// В приложении — `Contents/Resources/wordlists/switcher-rules.json`; в тестах и при разработке —
    /// `Data/switcher-rules.json`, который ищем вверх от этого файла.
    public static let shared: SwitcherRules = {
        var candidates: [URL] = []
        if let resources = Bundle.main.resourceURL {
            candidates.append(resources.appendingPathComponent("wordlists/switcher-rules.json"))
        }
        var dir = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        for _ in 0..<6 {
            candidates.append(dir.appendingPathComponent("Data/switcher-rules.json"))
            dir.deleteLastPathComponent()
        }
        for url in candidates where FileManager.default.fileExists(atPath: url.path) {
            do {
                return try JSONDecoder().decode(SwitcherRules.self, from: Data(contentsOf: url))
            } catch {
                fatalError("switcher-rules.json не читается: \(error)")
            }
        }
        fatalError("Нет switcher-rules.json")
    }()
}
