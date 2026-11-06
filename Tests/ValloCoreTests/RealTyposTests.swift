import Testing
import Foundation
@testable import ValloCore

/// Все формы слов: в репозитории сжаты — распаковываем один раз во временную папку.
let ruForms: SortedWordFile = {
    let out = FileManager.default.temporaryDirectory.appendingPathComponent("vallo-ru-forms.txt")
    let gz = repoURL("Data/wordlists/ru-forms.txt.gz")
    let p = Process()
    p.executableURL = URL(fileURLWithPath: "/bin/sh")
    p.arguments = ["-c", "/usr/bin/gzip -dc '\(gz.path)' > '\(out.path)'"]
    try! p.run(); p.waitUntilExit()
    return try! SortedWordFile(url: out)
}()

/// Исправлятор на настоящих опечатках из реальной переписки (Tests/Data/typos-real.tsv): сколько ловит и сколько портит.
@Suite struct RealTyposTests {
    struct Case { let typed: String; let expected: String? }   // nil — трогать нельзя

    static let cases: [Case] = try! String(contentsOf: repoURL("Tests/Data/typos-real.tsv"), encoding: .utf8)
        .split(separator: "\n").compactMap { line in
            guard !line.hasPrefix("#") else { return nil }
            let p = line.split(separator: "\t").map(String.init)
            guard p.count == 2 else { return nil }
            return Case(typed: p[0], expected: p[1] == "=" ? nil : p[1])
        }

    /// Как в приложении: словарь опечаток + «ё» + современные слова.
    static let corrector: TypoCorrector = {
        var t = typos
        t.yoWords = yo
        t.modernWords = Set(modern.map(Lexicon.normalize))
        t.forms = ruForms
        return t
    }()

    @Test func measure() {
        var fixed = 0, missed: [String] = [], wrong: [String] = [], damaged: [String] = []
        for c in Self.cases {
            let out = Self.corrector.correct(c.typed).map(Lexicon.normalize)
            if let expected = c.expected.map(Lexicon.normalize) {
                if out == expected { fixed += 1 }
                else if let out { wrong.append("\(c.typed)→\(out)") }
                else { missed.append(c.typed) }
            } else if let out {
                damaged.append("\(c.typed)→\(out)")
            }
        }
        let typosTotal = Self.cases.filter { $0.expected != nil }.count
        print("REAL TYPOS fixed \(fixed)/\(typosTotal), wrong \(wrong.count), missed \(missed.count), damaged \(damaged.count)/\(Self.cases.count - typosTotal)")
        print("REAL damaged: \(damaged.joined(separator: ", "))")
        print("REAL wrong: \(wrong.joined(separator: ", "))")
        print("REAL missed: \(missed.joined(separator: ", "))")
        // правильные слова и жаргон не портим вовсе; опечаток ловим не меньше, чем сейчас (2026-11-06: 126 из 144)
        #expect(damaged.isEmpty, "испортил: \(damaged)")
        #expect(fixed >= 126, "исправил только \(fixed)")
    }
}
