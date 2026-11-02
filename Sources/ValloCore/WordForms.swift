import Foundation

/// Список слов с формами в скобках: `чат(|а|у)` = чат, чата, чату (как в словаре «ё»). «#» — комментарий.
/// Так хранится `ru-modern.txt` — современные слова, которых нет в словарях субтитров и Википедии.
public enum WordForms {
    public static func load(url: URL) throws -> [String] {
        try String(contentsOf: url, encoding: .utf8).split(separator: "\n").flatMap { expand(String($0)) }
    }

    static func expand(_ line: String) -> [String] {
        let entry = line.split(separator: "#", maxSplits: 1, omittingEmptySubsequences: false)[0]
            .trimmingCharacters(in: .whitespaces)
        guard !entry.isEmpty else { return [] }
        guard let open = entry.firstIndex(of: "("), entry.hasSuffix(")") else { return [entry] }
        let stem = entry[..<open]
        let endings = entry[entry.index(after: open)..<entry.index(before: entry.endIndex)]
        return endings.split(separator: "|", omittingEmptySubsequences: false).map { String(stem + $0) }
    }
}
