import Testing
import Foundation
@testable import ValloCore

/// Общие примеры поведения свитчера: Data/tests/switcher-cases.tsv (формат — в Data/tests/README.md).
/// Тот же файл прогоняет Windows-версия своим симулятором.
struct SharedCase: Sendable, CustomTestStringConvertible {
    let line: Int
    let start: Language
    let keys: String
    let screen: String
    let note: String
    var testDescription: String { "строка \(line): \(note)" }
}

private func unescape(_ s: String) -> String {
    s.replacingOccurrences(of: "\\n", with: "\n").replacingOccurrences(of: "\\t", with: "\t")
}

func loadSharedCases() -> [SharedCase] {
    let text = try! String(contentsOf: repoURL("Data/tests/switcher-cases.tsv"), encoding: .utf8)
    var cases: [SharedCase] = []
    for (i, raw) in text.components(separatedBy: "\n").enumerated() {
        let line = raw.hasSuffix("\r") ? String(raw.dropLast()) : raw
        if line.trimmingCharacters(in: .whitespaces).isEmpty || line.hasPrefix("#") { continue }
        let f = line.components(separatedBy: "\t")
        precondition(f.count >= 3, "строка \(i + 1): ждём минимум 3 поля через табуляцию")
        precondition(f[0] == "en" || f[0] == "ru", "строка \(i + 1): start должен быть en или ru")
        cases.append(SharedCase(line: i + 1, start: f[0] == "ru" ? .ru : .en, keys: f[1],
                                screen: unescape(f[2]), note: f.count > 3 ? f[3] : ""))
    }
    return cases
}

private let fullEN = try! Lexicon.load(url: repoURL("Data/wordlists/en.txt"), language: .en)
private let fullRU = try! Lexicon.load(url: repoURL("Data/wordlists/ru.txt"), language: .ru,
                                       extra: WordForms.load(url: repoURL("Data/wordlists/ru-modern.txt")))

private enum Token { case key(Character), doubleShift, backspace, enter, tab, esc }

private func tokens(_ keys: String) -> [Token] {
    let known: [String: Token] = ["⇧⇧": .doubleShift, "⌫": .backspace, "⏎": .enter, "⇥": .tab, "ESC": .esc]
    var out: [Token] = []
    var i = keys.startIndex
    while i < keys.endIndex {
        if keys[i] == "{", let close = keys[i...].firstIndex(of: "}"),
           let t = known[String(keys[keys.index(after: i)..<close])] {
            out.append(t)
            i = keys.index(after: close)
        } else {
            out.append(.key(keys[i]))   // `{` вне известного токена — обычная клавиша (Shift+[)
            i = keys.index(after: i)
        }
    }
    return out
}

/// Экран-симулятор: что окажется в поле ввода.
func simulate(start: Language, keys: String) -> String {
    let pair = fixturePair("ansi")
    let engine = Engine(layouts: pair, judge: Judge(en: fullEN, ru: fullRU, learned: []), currentLanguage: start)
    var shown = start
    var screen = ""
    func apply(_ actions: [Action]) {
        for a in actions {
            switch a {
            case .delete(let n): screen.removeLast(min(n, screen.count))
            case .insert(let s): screen += s
            case .switchLayout(let l): shown = l
            case .pressKey(let code): screen += code == 36 ? "\n" : code == 48 ? "\t" : ""
            case .replaceSelection, .learn: break
            }
        }
    }
    func press(_ stroke: KeyStroke) {
        switch engine.handleKey(KeyEvent(stroke: stroke, hasCommandControlOrOption: false)) {
        case .passThrough:
            if stroke.keyCode == 51 { if !screen.isEmpty { screen.removeLast() } }
            else if stroke.keyCode == 36 { screen += "\n" }
            else if stroke.keyCode == 48 { screen += "\t" }
            else if stroke.keyCode != 53 { screen += pair.layout(shown).character(for: stroke) ?? "" }
        case .consume(let actions): apply(actions)
        }
    }
    for t in tokens(keys) {
        switch t {
        case .key(let ch): press(strokesTyping(String(ch), in: pair.en as! TableKeyLayout)[0])
        case .doubleShift: apply(engine.handleDoubleShift(selection: nil))
        case .backspace: press(KeyStroke(keyCode: 51))
        case .enter: press(KeyStroke(keyCode: 36))
        case .tab: press(KeyStroke(keyCode: 48))
        case .esc: press(KeyStroke(keyCode: 53))
        }
    }
    return screen
}

@Suite struct SharedCasesTests {
    @Test(arguments: loadSharedCases())
    func sharedCase(_ c: SharedCase) {
        let got = simulate(start: c.start, keys: c.keys)
        #expect(got == c.screen, "строка \(c.line) [\(c.note)]: keys=\(c.keys) ожидали «\(c.screen)», получили «\(got)»")
    }
}
