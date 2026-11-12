import Foundation
import ValloCore

/// Тексты слоёв из Фигмы — те же шаги, что у выделенного текста:
/// опечатки → «ё» → запятые (без заглавной и точки — в макете это решение редполитики) → типографика.
/// Одинаковые тексты обрабатываются один раз, ответы помнятся между нажатиями,
/// модель запятых работает в несколько потоков.
final class FigmaBatch {
    /// Сколько текстов модель читает одновременно.
    static let parallel = 4
    /// Больше стольких запомненных текстов — память сбрасывается.
    static let cacheLimit = 5000

    private let store: SettingsStore
    private let commas: CommaService
    private(set) var isRunning = false

    /// Текст слоя → результат и что в нём исправлено.
    private var cache: [String: (String, FixStats)] = [:]
    /// Настройки, при которых получены запомненные ответы: поменялись — ответы устарели.
    private var cacheSettings: Settings?

    private struct Settings: Equatable {
        let yo: Bool
        let ignored: Set<String>
    }

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

        let settings = Settings(yo: store.yoEnabled, ignored: typos.ignored)
        if settings != cacheSettings || cache.count > FigmaBatch.cacheLimit {
            cache.removeAll()
            cacheSettings = settings
        }

        // что ещё не обрабатывали — каждый текст по одному разу
        var todo: [String] = []
        var seen = Set<String>()
        for item in texts where cache[item.text] == nil && seen.insert(item.text).inserted {
            todo.append(item.text)
        }
        let remembered = texts.count - todo.count
        var fresh: [String: (String, FixStats)] = [:]
        var nextIndex = 0
        var inFlight = 0
        var finished = false

        func finish() {
            finished = true
            isRunning = false
            var edits: [FigmaTextEdits] = []
            var total = FixStats()
            for item in texts {
                guard let (result, stats) = fresh[item.text] ?? cache[item.text] else { continue }
                if result != item.text {
                    edits.append(FigmaTextEdits(id: item.id, edits: TextDiff.edits(from: item.text, to: result)))
                }
                total.add(stats)
            }
            log.debug("figma: batch \(texts.count) texts, \(todo.count) processed, \(remembered) remembered, \(edits.count) changed, \(Int(Date().timeIntervalSince(started) * 1000)) ms")
            completion(edits, total)
        }

        func launch() {
            guard !finished else { return }
            while inFlight < FigmaBatch.parallel, nextIndex < todo.count {
                let text = todo[nextIndex]
                nextIndex += 1
                inFlight += 1
                process(text, typos: typos, yo: yo) { [weak self] result, stats, keep in
                    fresh[text] = (result, stats)
                    if keep { self?.cache[text] = (result, stats) }
                    inFlight -= 1
                    launch()
                }
            }
            if inFlight == 0, nextIndex == todo.count { finish() }
        }
        launch()
    }

    /// Один текст. `completion` — на главном потоке, всегда асинхронно;
    /// `keep` — ответ можно запомнить (модель не опоздала).
    private func process(_ text: String, typos: TypoCorrector, yo: Yoficator,
                         completion: @escaping (String, FixStats, _ keep: Bool) -> Void) {
        let typoFixed = typos.correctPhrase(text)
        let yoFixed = store.yoEnabled ? yo.fixPhrase(typoFixed) : typoFixed
        let needsCommas = FigmaBatch.hasTwoWords(yoFixed)
        let done: (String?) -> Void = { withCommas in
            let (result, stats) = FixPipeline.finish(original: text, typoFixed: typoFixed,
                                                     yoFixed: yoFixed, withCommas: withCommas)
            completion(result, stats, withCommas != nil || !needsCommas)
        }
        // в одном слове запятой взяться неоткуда — модель не будим
        if needsCommas {
            commas.fix(yoFixed, typos: typos, capitalize: false, completion: done)
        } else {
            // через очередь — чтобы длинный пакет без модели не рос в глубину стека
            DispatchQueue.main.async { done(nil) }
        }
    }

    static func hasTwoWords(_ text: String) -> Bool {
        text.split(whereSeparator: { $0.isWhitespace }).filter { $0.contains(where: \.isLetter) }.count >= 2
    }
}
