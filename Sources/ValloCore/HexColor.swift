/// Цвет, набранный в русской раскладке: клавиши a–f дают ф, и, с, в, у, а, а Shift+3 — «№» вместо «#».
/// «№аааааа» → «#ffffff», «#1ф2и3с» → «#1a2b3c»; в поле цвета Фигмы — и без решётки: «ааа» → «fff».
/// Только 3, 6 или 8 знаков (#rgb, #rrggbb, #rrggbbaa). Решения автора 2026-11-06.
public enum HexColor {
    static let keys: [Character: Character] = [
        "ф": "a", "и": "b", "с": "c", "в": "d", "у": "e", "а": "f",
        "Ф": "A", "И": "B", "С": "C", "В": "D", "У": "E", "А": "F",
    ]

    /// Исправленный цвет или nil — это не цвет (или исправлять нечего).
    /// `requireHash` — нужна решётка (везде, кроме поля цвета Фигмы).
    public static func fix(_ token: String, requireHash: Bool) -> String? {
        var body = Substring(token)
        var prefix: Character?
        if let first = body.first, first == "#" || first == "№" {
            prefix = first
            body = body.dropFirst()
        } else if requireHash {
            return nil
        }
        guard [3, 6, 8].contains(body.count) else { return nil }
        var out = ""
        var russian = false, letter = false
        for ch in body {
            if let latin = keys[ch] {
                out.append(latin); russian = true; letter = true
            } else if ch.isASCII && ch.isHexDigit {
                out.append(ch); if ch.isLetter { letter = true }
            } else {
                return nil
            }
        }
        // «№ 123456» без букв — номер, а не цвет; «#abc» латиницей — уже правильно
        guard letter, russian || prefix == "№" else { return nil }
        return prefix == nil ? out : "#" + out
    }
}
