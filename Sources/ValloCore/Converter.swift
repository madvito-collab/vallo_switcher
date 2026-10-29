/// Переводчик: перекладывает текст с одной раскладки на другую по физическим клавишам.
public enum Converter {
    /// Что дадут эти нажатия в данной раскладке. Нажатия без символа пропускаются.
    public static func render(_ strokes: [KeyStroke], in layout: any KeyLayout) -> String {
        strokes.compactMap { layout.character(for: $0) }.joined()
    }

    /// Какими нажатиями набрать текст в данной раскладке. nil — символа в раскладке нет.
    public static func strokes(for text: String, in layout: any KeyLayout) -> [KeyStroke?] {
        let reverse = reverseTable(of: layout)
        return text.map { reverse[String($0)] }
    }

    /// Текст, набранный в `from`, как он выглядел бы в `to`. Символы, которых нет в `from`, остаются как есть.
    public static func convert(_ text: String, from: any KeyLayout, to: any KeyLayout) -> String {
        let reverse = reverseTable(of: from)
        return text.map { ch -> String in
            guard let stroke = reverse[String(ch)], let out = to.character(for: stroke) else { return String(ch) }
            return out
        }.joined()
    }

    /// символ → нажатие. Если символ дают несколько клавиш — без Caps Lock и с меньшим keyCode
    /// (основной блок, а не цифровая клавиатура).
    static func reverseTable(of layout: any KeyLayout) -> [String: KeyStroke] {
        var result: [String: KeyStroke] = [:]
        for keyCode in UInt16(0)...127 {
            for caps in [false, true] {
                for shift in [false, true] {
                    let stroke = KeyStroke(keyCode: keyCode, shift: shift, capsLock: caps)
                    guard let ch = layout.character(for: stroke) else { continue }
                    if let existing = result[ch], !isBetter(stroke, than: existing) { continue }
                    result[ch] = stroke
                }
            }
        }
        return result
    }

    private static func isBetter(_ a: KeyStroke, than b: KeyStroke) -> Bool {
        if a.capsLock != b.capsLock { return !a.capsLock }
        return a.keyCode < b.keyCode
    }
}
