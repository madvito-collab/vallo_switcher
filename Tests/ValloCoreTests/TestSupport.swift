import Foundation
@testable import ValloCore

/// Путь от корня репозитория (папка, где лежит Package.swift).
func repoURL(_ relative: String) -> URL {
    var url = URL(fileURLWithPath: #filePath)
    while !FileManager.default.fileExists(atPath: url.appendingPathComponent("Package.swift").path) {
        url.deleteLastPathComponent()
    }
    return url.appendingPathComponent(relative)
}

/// Раскладки EN и RU, снятые с системы, для клавиатуры "ansi" или "iso".
func fixturePair(_ kind: String) -> LayoutPair {
    let en = try! TableKeyLayout.load(url: repoURL("Data/layouts/\(kind)-en.json"))
    let ru = try! TableKeyLayout.load(url: repoURL("Data/layouts/\(kind)-ru.json"))
    return LayoutPair(en: en, ru: ru)
}

/// Нажатия, которые в данной раскладке дают этот текст (без capsLock).
/// Если символ дают несколько клавиш — берём основной блок (меньший keyCode),
/// а не цифровую клавиатуру.
func strokesTyping(_ text: String, in layout: TableKeyLayout) -> [KeyStroke] {
    text.map { ch in
        layout.table
            .filter { $0.value == String(ch) && !$0.key.capsLock }
            .map(\.key)
            .min { $0.keyCode < $1.keyCode }!
    }
}
