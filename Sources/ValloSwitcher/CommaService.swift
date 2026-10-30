import Foundation
import ValloCore

/// Запятые по сочетанию: держит модель (загружает лениво), режет текст на предложения,
/// прогоняет через модель на фоне и фильтрует ответ. Без модели, при ошибке или таймауте — nil.
final class CommaService: @unchecked Sendable {
    /// Ожидание ответа модели: 3 с, если она уже в памяти; при первом запуске — ещё и на загрузку.
    static let timeout: TimeInterval = 3
    static let coldTimeout: TimeInterval = 8
    /// Модель занимает ~500 МБ памяти — выгружаем, если ею не пользовались полчаса.
    static let idleUnload: TimeInterval = 30 * 60
    static let modelName = "sage-fredt5-95m"

    /// Куда распаковывается скачанная модель.
    static var modelDirectory: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Vallo Switcher/Models/\(modelName)")
    }

    static var isInstalled: Bool {
        FileManager.default.fileExists(atPath: modelDirectory.appendingPathComponent("decoder_model.onnx").path)
    }

    private let queue = DispatchQueue(label: "com.vallo.switcher.commas")
    /// Модель есть на диске, но не загрузилась (битые файлы) — на главном потоке.
    var onLoadFailed: (() -> Void)?
    private var model: CommaModel? {
        didSet { loadedLock.withLock { loaded = model != nil } }
    }
    /// Загружена ли модель — читается с главного потока без ожидания очереди.
    private var loaded = false
    private let loadedLock = NSLock()
    private var idleTimer: DispatchWorkItem?

    /// Модель удалили — выгрузить.
    func unload() {
        queue.async { self.model = nil }
    }

    /// Текст с запятыми (только безопасные правки) или nil. `completion` — на главном потоке.
    func fix(_ text: String, typos: TypoCorrector, capitalize: Bool, completion: @escaping (String?) -> Void) {
        guard CommaService.isInstalled else { return completion(nil) }
        var finished = false
        let finish: (String?) -> Void = { result in
            DispatchQueue.main.async {
                guard !finished else { return }
                finished = true
                completion(result)
            }
        }
        let cold = loadedLock.withLock { !loaded }
        DispatchQueue.main.asyncAfter(deadline: .now() + (cold ? CommaService.coldTimeout : CommaService.timeout)) { finish(nil) }

        queue.async { [self] in
            scheduleUnload()
            let semaphore = DispatchSemaphore(value: 0)
            if model == nil {
                Task {
                    model = try? await CommaModel.load(directory: CommaService.modelDirectory)
                    semaphore.signal()
                }
                semaphore.wait()
            }
            guard let model else {
                DispatchQueue.main.async { self.onLoadFailed?() }
                return finish(nil)
            }
            do {
                let lines = text.components(separatedBy: "\n").map { line -> String in
                    // пустые строки и строки без букв модели не нужны
                    guard line.contains(where: \.isLetter) else { return line }
                    return CommaService.sentences(line).map { sentence in
                        let raw = try? model.run(sentence)
                        return raw.map { CommaFilter.merge(original: sentence, model: $0, typos: typos, capitalize: capitalize) }
                            ?? sentence
                    }.joined(separator: " ")
                }
                finish(lines.joined(separator: "\n"))
            }
        }
    }

    /// Выгрузить модель, если ею не пользовались `idleUnload` (вызывается на очереди сервиса).
    private func scheduleUnload() {
        idleTimer?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.model = nil }
        idleTimer = work
        queue.asyncAfter(deadline: .now() + CommaService.idleUnload, execute: work)
    }

    /// Строку — на предложения (после . ! ? … и пробела), чтобы модель не обрезала длинный ответ.
    static func sentences(_ line: String) -> [String] {
        guard !line.trimmingCharacters(in: .whitespaces).isEmpty else { return [line] }
        var result: [String] = []
        var current = ""
        var chars = Array(line)[...]
        while let ch = chars.popFirst() {
            current.append(ch)
            if ".!?…".contains(ch), chars.first == " " {
                result.append(current)
                current = ""
                chars = chars.drop { $0 == " " }
            }
        }
        if !current.isEmpty { result.append(current) }
        return result
    }
}
