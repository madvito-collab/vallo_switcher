import Foundation
import Testing
@testable import ValloCore

@Suite struct FigmaBridgeCoreTests {
    func msg(_ json: String) -> FigmaMessage? { FigmaMessage.decode(Data(json.utf8)) }

    /// Применить правки к старой строке (с конца, в единицах UTF-16) — как это делает плагин.
    func apply(_ edits: [TextEdit], to old: String) -> String {
        var units = Array(old.utf16)
        for e in edits.reversed() {
            units.replaceSubrange(e.start..<e.end, with: Array(e.insert.utf16))
        }
        return String(decoding: units, as: UTF16.self)
    }

    // MARK: - Сообщения

    @Test func helloDecodes() {
        #expect(msg(#"{"type":"hello","version":1,"file":"Лендинг"}"#) == .hello(version: 1, file: "Лендинг", fresh: false))
        #expect(msg(#"{"type":"hello","version":1,"file":"Лендинг","fresh":true}"#) == .hello(version: 1, file: "Лендинг", fresh: true))
    }

    @Test func layersModeDecodes() {
        let m = msg(#"{"type":"mode","id":7,"mode":"layers","texts":[{"id":"1:2","text":"а"},{"id":"1:3","text":"б"}],"skipped":3}"#)
        #expect(m == .mode(id: 7, mode: .layers([FigmaText(id: "1:2", text: "а"), FigmaText(id: "1:3", text: "б")]), skipped: 3))
        #expect(msg(#"{"type":"mode","id":8,"mode":"text"}"#) == .mode(id: 8, mode: .text, skipped: 0))
        #expect(msg(#"{"type":"mode","id":9,"mode":"empty"}"#) == .mode(id: 9, mode: .empty, skipped: 0))
        #expect(msg(#"{"type":"done","id":9}"#) == .done(id: 9))
    }

    @Test func resultRoundTrip() {
        var stats = FixStats()
        stats.commas = 2
        stats.layers = 1
        let m = FigmaMessage.result(id: 3, edits: [FigmaTextEdits(id: "1:2", edits: [TextEdit(start: 1, end: 2, insert: nb)])],
                                    stats: stats, skipped: 1)
        #expect(FigmaMessage.decode(m.encode()) == m)
        #expect(FigmaMessage.decode(FigmaMessage.press(id: 5).encode()) == .press(id: 5))
    }

    @Test func garbageIsNil() {
        #expect(FigmaMessage.decode(Data("{".utf8)) == nil)
        #expect(msg(#"{"type":"zzz"}"#) == nil)
    }

    // MARK: - Правки

    @Test func diffEqualIsEmpty() {
        #expect(TextDiff.edits(from: "abc", to: "abc") == [])
    }

    @Test func diffLocalNbsp() {
        let edits = TextDiff.edits(from: "в доме и в саду", to: "в\(nb)доме и\(nb)в\(nb)саду")
        #expect(edits.count == 3)
        #expect(edits.allSatisfy { $0.end - $0.start == 1 && $0.insert == nb })
    }

    @Test func diffUtf16Offsets() {
        let edits = TextDiff.edits(from: "👍 \"да\"", to: "👍 «да»")
        #expect(edits.map(\.start) == [3, 6])
    }

    @Test func applyReproducesNew() {
        let pairs = [
            ("в доме и в саду", "в\(nb)доме и\(nb)в\(nb)саду"),
            ("мир", "Новый мир"),
            ("конец...", "конец"),
            ("abc", "xyz"),
            ("👍 \"да\" - нет", "👍 «да»\(nb)— нет"),
            ("", "текст"),
            ("текст", ""),
        ]
        for (old, new) in pairs {
            #expect(apply(TextDiff.edits(from: old, to: new), to: old) == new)
        }
    }

    @Test func longLayerStillLocalEdits() {
        // длинный слой (за пределом таблицы LCS) — правки всё равно точечные, иначе слетит оформление
        let line = String(repeating: "слово ", count: 300)
        let old = "\"начало\" " + line + "\n" + line + "\n" + line + "конец - да"
        let new = "«начало» " + line + "\n" + line + "\n" + line + "конец\(nb)— да"
        let edits = TextDiff.edits(from: old, to: new)
        #expect(apply(edits, to: old) == new)
        #expect(edits.allSatisfy { $0.end - $0.start <= 3 })
    }

    @Test func editsAreSortedAndDisjoint() {
        let edits = TextDiff.edits(from: "a - b \"c\" ...", to: "a\(nb)— b «c» …")
        for (a, b) in zip(edits, edits.dropFirst()) { #expect(a.end <= b.start) }
    }

    // MARK: - Счётчики

    @Test func counters() {
        #expect(FixCounter.commas("а б", "а, б") == 1)
        #expect(FixCounter.yo("еще", "ещё") == 1)
        #expect(FixCounter.typos("помоему тут", "по-моему тут") == 1)
        #expect(FixCounter.typos("всё тут", "всё тут") == 0)
        let t = FixCounter.typography("a - b \"c\"", "a\(nb)— b «c»")
        #expect(t.nbsp == 1 && t.dashes == 1 && t.quotes == 2 && t.other == 0)
        #expect(FixCounter.typography("ну...", "ну…").other == 1)
    }

    @Test func statsAdd() {
        var a = FixStats()
        #expect(a.isEmpty)
        var b = FixStats()
        b.yo = 2
        b.layers = 1
        a.add(b)
        a.add(b)
        #expect(a.yo == 4 && a.layers == 2 && !a.isEmpty)
        var onlyLayers = FixStats()
        onlyLayers.layers = 3
        #expect(onlyLayers.isEmpty)
    }

    // MARK: - Конвейер для слоя

    @Test func pipelineKeepsCaseAndNoPeriod() {
        // в Фигме модель вызывается с capitalize: false — ни заглавной, ни точки в конце
        let merged = CommaFilter.merge(original: "привет как дела", model: "Привет, как дела.", typos: typos, capitalize: false)
        let (text, stats) = FixPipeline.finish(original: "привет как дела", typoFixed: "привет как дела",
                                               yoFixed: "привет как дела", withCommas: merged)
        #expect(text == "привет, как дела")
        #expect(stats.commas == 1)
    }

    @Test func pipelineKeepsLineSeparators() {
        let original = "Заголовок\nпервая строка\u{2028}вторая"
        let (text, _) = FixPipeline.finish(original: original, typoFixed: original, yoFixed: original, withCommas: nil)
        #expect(text.contains("\n") && text.contains("\u{2028}"))
    }

    @Test func pipelineStats() {
        let (text, stats) = FixPipeline.finish(original: "помоему еще - да", typoFixed: "по-моему еще - да",
                                               yoFixed: "по-моему ещё - да", withCommas: nil)
        #expect(text == "по-моему ещё\(nb)— да")
        #expect(stats.typos == 1 && stats.yo == 1 && stats.dashes == 1 && stats.nbsp >= 1 && stats.layers == 1)
        let (same, none) = FixPipeline.finish(original: "да", typoFixed: "да", yoFixed: "да", withCommas: nil)
        #expect(same == "да" && none.isEmpty && none.layers == 0)
    }
}
