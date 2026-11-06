import Foundation

/// Телефоны и разряды чисел — часть типографики (решения автора 2026-11-06).
/// Телефон: +7 или 8 и ровно 10 цифр после: мобильный (код на 9) → «+7 916 123-45-67», городской → «+7 (812) 555-55-55»,
/// «8 800» остаётся с восьмёркой. Разряды: от 5 цифр всегда, четыре — только рядом с валютой (не трогаем годы).
/// На лету (`final: false`) число должно быть закончено — после него уже стоит пробел или знак.
enum NumbersPhones {
    static let nbsp = Typographer.nbsp
    /// Буква, но не метка «слева неизвестно что»: номер в начале поля после клика — тоже номер.
    static let letter = "[\\p{L}&&[^\(Typographer.unknownLeft)]]"
    private static let sep = "[ \\u00A0\\-()]*"

    // MARK: - Телефоны

    nonisolated(unsafe) private static let phoneRegex = try! NSRegularExpression(
        pattern: "(?<![\(letter)\\d+#№@/._])(\\+7|8)\(sep)(\\d)\(sep)(\\d)\(sep)(\\d)" + String(repeating: "\(sep)(\\d)", count: 7)
            + "(?![\\d\\p{L}@/_])")

    static func phones(_ s: String, final: Bool) -> String {
        replace(phoneRegex, in: s, final: final) { m, ns in
            let prefix = ns.substring(with: m.range(at: 1))
            let d = (2...11).map { ns.substring(with: m.range(at: $0)) }.joined()
            let code = String(d.prefix(3)), a = String(d.dropFirst(3).prefix(3)),
                b = String(d.dropFirst(6).prefix(2)), c = String(d.dropFirst(8))
            let tail = "\(a)-\(b)-\(c)"
            if code == "800" { return "\(prefix == "8" ? "8" : "+7")\(nbsp)800\(nbsp)\(tail)" }
            if code.hasPrefix("9") { return "+7\(nbsp)\(code)\(nbsp)\(tail)" }
            return "+7\(nbsp)(\(code))\(nbsp)\(tail)"
        }
    }

    // MARK: - Разряды

    /// Слева от числа нельзя: цифра, буква, знаки адресов и кодов (№, #, @, /, _, :), точка или запятая
    /// (дробная часть, версия), знак плюс; «№ 12345» — номер, не сумма.
    private static let before = "(?<![\\d\(letter)#№@/_:.,+])(?<!№[ \\u00A0])"
    nonisolated(unsafe) private static let bigRegex = try! NSRegularExpression(
        pattern: "\(before)([1-9]\\d{4,9})(?![\\d\\p{L}@/_:])")
    /// Четыре цифры — с валютой после («5000 ₽») или перед («$1500»).
    nonisolated(unsafe) private static let moneyAfterRegex = try! NSRegularExpression(
        pattern: "\(before)([1-9]\\d{3})(?=[ \\u00A0]?[₽$€])")
    nonisolated(unsafe) private static let moneyBeforeRegex = try! NSRegularExpression(
        pattern: "(?<=[$€₽])[ \\u00A0]?([1-9]\\d{3})(?![\\d\\p{L}])")

    static func groups(_ s: String, final: Bool) -> String {
        var out = replace(bigRegex, in: s, final: final) { m, ns in grouped(ns.substring(with: m.range(at: 1))) }
        out = replace(moneyAfterRegex, in: out, final: true) { m, ns in grouped(ns.substring(with: m.range(at: 1))) }
        out = replace(moneyBeforeRegex, in: out, final: final) { m, ns in grouped(ns.substring(with: m.range(at: 1))) }
        return out
    }

    /// «1234567» → «1 234 567» (неразрывные пробелы).
    static func grouped(_ digits: String) -> String {
        var parts: [String] = []
        var rest = Substring(digits)
        while rest.count > 3 {
            parts.insert(String(rest.suffix(3)), at: 0)
            rest = rest.dropLast(3)
        }
        parts.insert(String(rest), at: 0)
        return parts.joined(separator: nbsp)
    }

    // MARK: -

    /// Замены с конца, чтобы не съезжали позиции. На лету совпадение у самого конца текста ждёт следующего символа.
    private static func replace(_ regex: NSRegularExpression, in s: String, final: Bool,
                                _ make: (NSTextCheckingResult, NSString) -> String) -> String {
        let ns = s as NSString
        var out = s as NSString
        for m in regex.matches(in: s, range: NSRange(location: 0, length: ns.length)).reversed() {
            if !final && m.range.location + m.range.length >= ns.length { continue }
            let full = m.range
            // группа 1 — то, что заменяем; для валюты перед числом пробел перед цифрами тоже уходит
            let target = regex === moneyBeforeRegex ? full : (m.numberOfRanges > 1 && regex !== phoneRegex ? m.range(at: 1) : full)
            out = out.replacingCharacters(in: target, with: make(m, ns)) as NSString
        }
        return out as String
    }
}
