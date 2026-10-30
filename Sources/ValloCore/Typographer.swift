import Foundation

/// Типографика по «Ководству» Лебедева: тире, минусы, кавычки, неразрывные пробелы, символы.
/// Одна функция на оба режима: выделенный текст целиком (`final: true`) и набор на лету (`final: false`),
/// где правила, которым нужен следующий символ, ждут его.
public enum Typographer {
    static let nbsp = "\u{00A0}"

    /// Предлоги, союзы и частицы, после которых ставится неразрывный пробел (1–2 буквы + трёхбуквенные предлоги).
    static let shortWords = ["а", "в", "во", "и", "к", "ко", "о", "об", "от", "с", "со", "у", "на", "по", "за", "до",
                             "из", "но", "не", "ни", "для", "без", "под", "над", "при", "про", "через"]
    /// Частицы, перед которыми ставится неразрывный пробел.
    static let particles = ["ли", "ль", "же", "ж", "бы", "б"]

    /// `hint` — язык для кавычек, если рядом нет букв (на лету: текущая раскладка).
    public static func format(_ text: String, final: Bool = true, hint: Language = .ru) -> String {
        var s = text
        for rule in rules(final: final) {
            s = rule.regex.stringByReplacingMatches(in: s, range: NSRange(s.startIndex..., in: s), withTemplate: rule.template)
        }
        return quotes(s, hint: hint)
    }

    // MARK: - Правила-замены

    private struct Rule {
        let regex: NSRegularExpression
        let template: String
    }

    nonisolated(unsafe) private static var cache: [Bool: [Rule]] = [:]
    private static let lock = NSLock()

    private static func rules(final: Bool) -> [Rule] {
        lock.lock()
        defer { lock.unlock() }
        if let cached = cache[final] { return cached }
        // $END — «или конец текста», только когда текст закончен
        let end = final ? "|\\z" : ""
        let sp = "[ \\u00A0]"
        let words = shortWords.joined(separator: "|")
        let parts = particles.joined(separator: "|")
        let raw: [(String, String)] = [
            // чистка пробелов
            ("(?<=\\S) {2,}", " "),
            (" +(?=[,.!?:;])", ""),
            // символы
            ("(?i)\\([cс]\\)", "©"),
            ("(?i)\\(r\\)", "®"),
            ("(?i)\\(tm\\)", "™"),
            ("\\+-", "±"),
            ("->", "→"),
            ("<-", "←"),
            ("(?<![\\d/])1/2(?=[^\\d/]\(end))", "½"),
            // умножение: 5x3, 5*3, 5 х 3 (пробел мог уже стать неразрывным — «число + слово»)
            ("(?<=\\d)[xх*](?=\\d)", "×"),
            // хоть с одной стороны пробел — ставим с обеих
            ("(\\d)(?:[ \\u00A0][xх*][ \\u00A0]?|[xх*][ \\u00A0])(?=\\d)", "$1 × "),
            ("(?<!\\.)\\.{3}(?!\\.)", "…"),
            // цифры: диапазон (не даты и не телефоны), минус
            ("(?<![\\d+\\-–])(\\d{1,4})-(\\d{1,4})(?=[^\\d\\-]\(end))", "$1–$2"),
            ("(\\d) - (?=\\d)", "$1 − "),
            ("(^|[ \\u00A0(])-(?=\\d)", "$1−"),
            // тире: в начале строки (диалог), между словами; после цифры — только если справа не цифра
            ("(^|\\n)-{1,2} ", "$1— "),
            ("([^\\s\\d])\(sp)+-{1,2}\(sp)+", "$1\(nbsp)— "),
            ("(\\d)\(sp)+-{1,2}\(sp)+(?=[^\\d\\s]\(end))", "$1\(nbsp)— "),
            ("([^\\s]) —", "$1\(nbsp)—"),
            // неразрывные пробелы
            ("(?i)(?<![\\p{L}\\p{N}'’\\-])(\(words)) ", "$1\(nbsp)"),
            ("(?i)(?<=\\p{L}) (\(parts))(?=[\\s.,!?;:…)»“”]\(end))", "\(nbsp)$1"),
            ("(?<=\\d) (?=\\p{L})", nbsp),
            ("(?<=\\d) (?=\\d{3}(?!\\d))", nbsp),
            ("(?<![\\p{L}])(\\p{Lu})\\. (?=\\p{Lu})", "$1.\(nbsp)"),
            ("([№§]) ", "$1\(nbsp)"),
        ]
        let compiled = raw.map { Rule(regex: try! NSRegularExpression(pattern: $0.0, options: [.anchorsMatchLines]),
                                      template: $0.1) }
        cache[final] = compiled
        return compiled
    }

    // MARK: - Кавычки

    /// Прямые кавычки → «ёлочки» с „лапками“ внутри (русский) или “лапки” с ‘одинарными’ (английский).
    /// Апостроф между буквами → ’. Уже поставленные кавычки учитываются во вложенности.
    static func quotes(_ text: String, hint: Language) -> String {
        var chars = Array(text)
        guard chars.contains(where: { $0 == "\"" || $0 == "'" }) else { return text }
        var stack: [Character] = []          // открытые кавычки
        for i in chars.indices {
            let c = chars[i]
            let prev: Character? = i > 0 ? chars[i - 1] : nil
            switch c {
            case "'":
                if let prev, prev.isLetter, i + 1 < chars.count, chars[i + 1].isLetter { chars[i] = "’" }
            case "«", "„":
                stack.append(c)
            case "»":
                if stack.last == "«" { stack.removeLast() }
            case "“":
                // в русском “ закрывает „, в английском — открывает
                if stack.last == "„" { stack.removeLast() } else { stack.append("“") }
            case "”":
                if stack.last == "“" { stack.removeLast() }
            case "‘":
                stack.append("‘")
            case "’":
                if stack.last == "‘" { stack.removeLast() }
            case "\"":
                if isOpeningPosition(prev) || stack.isEmpty {
                    let open: Character
                    switch language(around: chars, at: i, hint: hint) {
                    case .ru: open = stack.isEmpty ? "«" : "„"
                    case .en: open = stack.isEmpty ? "“" : "‘"
                    }
                    chars[i] = open
                    stack.append(open)
                } else {
                    let opener = stack.removeLast()
                    chars[i] = ["«": "»", "„": "“", "“": "”", "‘": "’"][opener] ?? "»"
                }
            default:
                break
            }
        }
        return String(chars)
    }

    private static func isOpeningPosition(_ prev: Character?) -> Bool {
        guard let prev else { return true }
        return prev.isWhitespace || "([{—–-«„".contains(prev)
    }

    /// Язык по ближайшей букве слева, а если слева букв нет — справа.
    private static func language(around chars: [Character], at i: Int, hint: Language) -> Language {
        let left = chars[..<i].last(where: \.isLetter)
        let right = chars[(i + 1)...].first(where: \.isLetter)
        guard let letter = left ?? right else { return hint }
        return Lexicon.isInAlphabet(Character(letter.lowercased()), .ru) ? .ru : .en
    }
}
