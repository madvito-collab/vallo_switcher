import Foundation
import ValloCore

/// Тексты слоёв из Фигмы — по одному, те же шаги, что у выделенного текста:
/// опечатки → «ё» → запятые (без заглавной и точки — в макете это решение редполитики) → типографика.
final class FigmaBatch {
    private let store: SettingsStore
    private let commas: CommaService
    private(set) var isRunning = false

    init(store: SettingsStore, commas: CommaService) {
        self.store = store
        self.commas = commas
    }

    /// `completion` — на главном потоке: правки только для изменившихся слоёв и общая статистика.
    func run(_ texts: [FigmaText], typos: TypoCorrector, yo: Yoficator,
             completion: @escaping ([FigmaTextEdits], FixStats) -> Void) {
        guard !isRunning else {
            log.debug("figma: batch already running — press ignored")
            return
        }
        isRunning = true
        let started = Date()
        var edits: [FigmaTextEdits] = []
        var total = FixStats()
        var index = 0

        func next() {
            guard index < texts.count else {
                isRunning = false
                log.debug("figma: batch \(texts.count) texts, \(edits.count) changed, \(Int(Date().timeIntervalSince(started) * 1000)) ms")
                completion(edits, total)
                return
            }
            let item = texts[index]
            index += 1
            let typoFixed = typos.correctPhrase(item.text)
            let yoFixed = store.yoEnabled ? yo.fixPhrase(typoFixed) : typoFixed
            let finish: (String?) -> Void = { withCommas in
                let (result, stats) = FixPipeline.finish(original: item.text, typoFixed: typoFixed,
                                                         yoFixed: yoFixed, withCommas: withCommas)
                if result != item.text {
                    edits.append(FigmaTextEdits(id: item.id, edits: TextDiff.edits(from: item.text, to: result)))
                }
                total.add(stats)
                // через очередь — чтобы длинный пакет без модели не рос в глубину стека
                DispatchQueue.main.async { next() }
            }
            // в одном слове запятой взяться неоткуда — модель не будим
            if FigmaBatch.hasTwoWords(yoFixed) {
                commas.fix(yoFixed, typos: typos, capitalize: false, completion: finish)
            } else {
                finish(nil)
            }
        }
        next()
    }

    static func hasTwoWords(_ text: String) -> Bool {
        text.split(whereSeparator: { $0.isWhitespace }).filter { $0.contains(where: \.isLetter) }.count >= 2
    }
}
