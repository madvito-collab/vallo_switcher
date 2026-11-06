/// Смайлики из скобок: «ок))» + пробел → «ок😄». Наборы — «Миллениал», «Зумер» или свой.
/// Правила:
/// - срабатывает на пробеле, Enter или Tab после скобок — раньше не знаем, будет ли ещё скобка;
/// - `)` — не смайлик, если закрывает открытую в этой строке `(` («(вот так)») или стоит после цифры («1) пункт»);
/// - `(` — смайлик только с пробелом после: в тексте открывающая прилипает к слову («(вот»);
/// - `:)`, `;(` и подобные — уже смайлики, не трогаем;
/// - скобок больше, чем в наборе, — берём самый длинный из того же вида («)))))» → как «)))»).
public struct EmojiBrackets: Sendable, Equatable {
    /// Скобки → смайлик. Ключ — только из `(` или только из `)`.
    public let pairs: [String: String]

    public init(pairs: [String: String]) {
        self.pairs = pairs.filter { key, value in
            !value.isEmpty && !key.isEmpty && (key.allSatisfy { $0 == ")" } || key.allSatisfy { $0 == "(" })
        }
    }

    public static let millennial = EmojiBrackets(pairs: [
        ")": "🙂", "))": "😄", ")))": "😂",
        "(": "🙁", "((": "😢", "(((": "😭",
    ])
    public static let zoomer = EmojiBrackets(pairs: [
        ")": "😊", "))": "😭", ")))": "💀",
        "(": "🥲", "((": "😔", "(((": "🥺",
    ])

    static let boundaries: Set<Character> = [" ", "\n", "\r", "\t"]
    /// Перед скобкой — уже смайлик из знаков (`:)`, `;(`, `=)`, `^)`).
    static let emoticonEyes: Set<Character> = [":", ";", "=", "^", "8", "-"]

    /// `raw` — текст, только что дополненный границей. Скобки перед границей стали смайликом — новый текст, иначе nil.
    /// Первые `frozen` символов не трогаем (человек отменил правку).
    public func apply(_ raw: [Character], frozen: Int = 0) -> [Character]? {
        guard let boundary = raw.last, EmojiBrackets.boundaries.contains(boundary) else { return nil }
        let body = Array(raw.dropLast())
        guard let bracket = body.last, bracket == ")" || bracket == "(" else { return nil }
        var start = body.count - 1
        while start > 0 && body[start - 1] == bracket { start -= 1 }
        guard start >= frozen else { return nil }
        let before = start > 0 ? body[start - 1] : nil
        if let b = before {
            if EmojiBrackets.emoticonEyes.contains(b) { return nil }
            if bracket == ")" && b.isNumber { return nil }
        }
        if bracket == ")" {
            // закрывает открытую в этой строке скобку — это текст
            let line = body[..<start].split(omittingEmptySubsequences: false) { $0 == "\n" || $0 == "\r" }.last ?? []
            let open = line.filter { $0 == "(" }.count, close = line.filter { $0 == ")" }.count
            if open > close { return nil }
        }
        guard let emoji = emoji(for: body.count - start, bracket) else { return nil }
        return Array(body[..<start]) + Array(emoji) + [boundary]
    }

    /// Ровно столько скобок — или самый длинный из того же вида, что короче.
    private func emoji(for count: Int, _ bracket: Character) -> String? {
        let best = pairs.keys.filter { $0.first == bracket && $0.count <= count }.max { $0.count < $1.count }
        return best.flatMap { pairs[$0] }
    }
}
