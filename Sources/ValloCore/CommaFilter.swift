import Foundation

/// Фильтр ответа модели запятых: берём только безопасное.
/// Запятые и знаки — да; замену слова — только если её принял бы исправлятор опечаток (или е→ё, дефис частицы);
/// добавленные и удалённые слова — никогда; точку между цифрами — никогда;
/// заглавную в начале и точку в конце — только если `capitalize`.
public enum CommaFilter {
    static let allowedMarks: Set<Character> = [",", "?", "!", ":", ";"]

    public static func merge(original: String, model: String, typos: TypoCorrector, capitalize: Bool) -> String {
        let ot = original.split(whereSeparator: \.isWhitespace).map(String.init)
        let mt = model.split(whereSeparator: \.isWhitespace).map(String.init)
        var out: [String] = []

        for op in diff(ot.map(key), mt.map(key)) {
            switch op {
            case let .equal(i, j):
                out.append(sameWord(original: ot[i], model: mt[j]))
            case let .replace(i1, i2, j1, j2):
                out.append(contentsOf: replace(Array(ot[i1..<i2]), Array(mt[j1..<j2]), typos: typos))
            case .insert:
                break   // модель добавила слово — не принимаем
            case let .delete(i1, i2):
                out.append(contentsOf: ot[i1..<i2])
            }
        }

        var s = out.joined(separator: " ")
        s = s.replacingOccurrences(of: "(\\d)\\.\\s+(\\d)", with: "$1 $2", options: .regularExpression)
        if !capitalize {
            if s.hasSuffix(".") && !original.hasSuffix(".") { s.removeLast() }
            if let first = original.first, first.isLowercase, let f = s.first {
                s = String(f).lowercased() + s.dropFirst()
            }
        } else if let f = s.first {
            s = String(f).uppercased() + s.dropFirst()
            if let last = s.last, !".!?…".contains(last) { s += "." }
        }
        // запятая в конце, оставшаяся от отброшенного слова
        while s.hasSuffix(",") && !original.hasSuffix(",") { s.removeLast() }
        return s
    }

    // MARK: - Слова

    /// Ключ сравнения: без знаков по краям, строчные, ё → е, без дефиса.
    static func key(_ t: String) -> String {
        Lexicon.normalize(core(t)).replacingOccurrences(of: "-", with: "")
    }

    static func core(_ t: String) -> String {
        let chars = Array(t)
        guard let first = chars.firstIndex(where: { $0.isLetter || $0.isNumber }),
              let last = chars.lastIndex(where: { $0.isLetter || $0.isNumber }) else { return "" }
        return String(chars[first...last])
    }

    /// Знаки после слова у модели — только разрешённые.
    static func marks(_ t: String) -> String {
        let chars = Array(t)
        guard let last = chars.lastIndex(where: { $0.isLetter || $0.isNumber }) else { return "" }
        return String(chars[(last + 1)...].filter { allowedMarks.contains($0) })
    }

    /// Начало слова у человека (знаки перед словом сохраняем как были).
    static func lead(_ t: String) -> String {
        String(t.prefix { !($0.isLetter || $0.isNumber) })
    }

    /// Регистр первой буквы — как у человека.
    static func withLeadingCase(of original: String, _ word: String) -> String {
        guard let o = core(original).first, let w = word.first else { return word }
        return (o.isUppercase ? String(w).uppercased() : String(w).lowercased()) + word.dropFirst()
    }

    /// То же слово: знаки — модели; написание — модели, если отличается только ё или дефисом.
    static func sameWord(original: String, model: String) -> String {
        let o = core(original), m = core(model)
        let word = o.lowercased() == m.lowercased() ? o : withLeadingCase(of: original, m)
        return lead(original) + word + marks(model)
    }

    static func replace(_ o: [String], _ m: [String], typos: TypoCorrector) -> [String] {
        // «какой то» → «какой-то»
        if o.count == 2, m.count == 1, TypoCorrector.particles.contains(Lexicon.normalize(core(o[1]))),
           TypoCorrector.particleStems.contains(Lexicon.normalize(core(o[0]))),
           key(m[0]) == key(o[0]) + key(o[1]) {
            return [lead(o[0]) + withLeadingCase(of: o[0], core(m[0])) + marks(m[0])]
        }
        // замена слово на слово — только если исправлятор опечаток согласен
        if o.count == m.count {
            return zip(o, m).map { ow, mw in
                if let fixed = typos.correct(core(ow)), key(fixed) == key(mw) {
                    return lead(ow) + fixed + marks(mw)
                }
                return ow + commaOnly(mw)
            }
        }
        // модель разбила или склеила слова — слова оставляем свои, берём только запятую после куска
        guard let last = o.last, let modelLast = m.last else { return o }
        return o.dropLast() + [last + commaOnly(modelLast)]
    }

    /// От отвергнутого слова модели берём только запятую: «СП:» вместо «спс» не должен оставить двоеточие.
    static func commaOnly(_ t: String) -> String {
        marks(t).contains(",") ? "," : ""
    }

    // MARK: - Выравнивание слов

    enum Op {
        case equal(Int, Int)
        case replace(Int, Int, Int, Int)
        case insert(Int, Int)
        case delete(Int, Int)
    }

    /// Выравнивание двух списков по наибольшей общей подпоследовательности; несовпавшие куски —
    /// замена, вставка или удаление.
    static func diff(_ a: [String], _ b: [String]) -> [Op] {
        let n = a.count, m = b.count
        var lcs = Array(repeating: Array(repeating: 0, count: m + 1), count: n + 1)
        for i in stride(from: n - 1, through: 0, by: -1) {
            for j in stride(from: m - 1, through: 0, by: -1) {
                lcs[i][j] = a[i] == b[j] ? lcs[i + 1][j + 1] + 1 : max(lcs[i + 1][j], lcs[i][j + 1])
            }
        }
        var ops: [Op] = []
        var i = 0, j = 0, gapA = 0, gapB = 0
        func flushGap() {
            let i1 = i - gapA, j1 = j - gapB
            if gapA > 0 && gapB > 0 { ops.append(.replace(i1, i, j1, j)) }
            else if gapA > 0 { ops.append(.delete(i1, i)) }
            else if gapB > 0 { ops.append(.insert(j1, j)) }
            gapA = 0; gapB = 0
        }
        while i < n || j < m {
            if i < n, j < m, a[i] == b[j] {
                flushGap()
                ops.append(.equal(i, j))
                i += 1; j += 1
            } else if j < m && (i == n || lcs[i][j + 1] >= lcs[i + 1][j]) {
                j += 1; gapB += 1
            } else {
                i += 1; gapA += 1
            }
        }
        flushGap()
        return ops
    }
}
