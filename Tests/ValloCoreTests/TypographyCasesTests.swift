import Testing
import Foundation
@testable import ValloCore

/// Общие примеры типографики (`Data/tests/typography-cases.tsv`) — те же гоняет Windows. Мак — эталон.
@Suite struct TypographyCasesTests {
    struct Case { let mode: String; let input: String; let output: String; let note: String }

    static let cases: [Case] = try! String(contentsOf: repoURL("Data/tests/typography-cases.tsv"), encoding: .utf8)
        .split(separator: "\n", omittingEmptySubsequences: true).compactMap { line in
            guard !line.hasPrefix("#") else { return nil }
            let p = line.split(separator: "\t", omittingEmptySubsequences: false).map {
                String($0).replacingOccurrences(of: "⍽", with: "\u{00A0}")
            }
            return p.count == 4 ? Case(mode: p[0], input: p[1], output: p[2], note: p[3]) : nil
        }

    /// Набор по символу: что окажется на экране (типографика + смайлики «Миллениал»).
    static func live(_ text: String) -> String {
        var live = LiveTypographer()
        live.reset(knownLineStart: true)
        let options = LiveOptions(typography: true, typos: nil, emoji: .millennial)
        var screen: [Character] = []
        for c in text {
            if let e = live.typed(c, options: options) {
                screen.removeLast(e.delete); screen += Array(e.insert)
            } else {
                screen.append(c)
            }
        }
        return String(screen)
    }

    @Test func allCases() {
        #expect(Self.cases.count >= 80)
        for c in Self.cases {
            let got: String
            switch c.mode {
            case "final": got = Typographer.format(c.input)
            case "draft": got = Typographer.format(c.input, final: false)
            case "live": got = Self.live(c.input)
            default: Issue.record("неизвестный режим \(c.mode)"); continue
            }
            #expect(got == c.output, "\(c.mode) «\(c.input)» → «\(got)», ждали «\(c.output)» — \(c.note)")
        }
    }
}
