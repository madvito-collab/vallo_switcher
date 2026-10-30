import CryptoKit
import Foundation

/// Скачивание, проверка, распаковка и удаление модели запятых.
/// Сеть свитчер использует только здесь — по кнопке «Скачать».
final class ModelStore: NSObject, ObservableObject, URLSessionDownloadDelegate {
    enum State: Equatable {
        case notDownloaded
        case downloading(Double)
        case ready
        case failed(String)
    }

    static let url = URL(string: "https://github.com/madvito-collab/vallo-models/releases/download/models-1/sage-fredt5-95m.zip")!
    static let sha256 = "0c4af0f7f3905600c7d83eaafb27134f5d051fc80813b7be66ea61c0eba09486"
    static let downloadSize = "228 МБ"
    static let diskSize = "470 МБ"

    @Published private(set) var state: State
    /// Модель удалили или заменили — сервису запятых надо выгрузить старую.
    var onChange: (() -> Void)?

    private var session: URLSession?
    private var task: URLSessionDownloadTask?

    override init() {
        state = CommaService.isInstalled ? .ready : .notDownloaded
        super.init()
    }

    func download() {
        cancel()
        state = .downloading(0)
        let session = URLSession(configuration: .default, delegate: self, delegateQueue: .main)
        self.session = session
        task = session.downloadTask(with: ModelStore.url)
        task?.resume()
    }

    func cancel() {
        task?.cancel()
        task = nil
        session?.invalidateAndCancel()
        session = nil
        if case .downloading = state { state = CommaService.isInstalled ? .ready : .notDownloaded }
    }

    func delete() {
        cancel()
        try? FileManager.default.removeItem(at: CommaService.modelDirectory)
        state = .notDownloaded
        onChange?()
    }

    /// Модель не загрузилась (битые файлы) — предложить скачать заново.
    func markBroken() {
        // только если модель считалась готовой: во время удаления или скачивания это не поломка
        guard state == .ready else { return }
        try? FileManager.default.removeItem(at: CommaService.modelDirectory)
        state = .failed("Файлы модели повреждены — скачайте заново")
    }

    // MARK: - URLSessionDownloadDelegate

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64,
                    totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        guard totalBytesExpectedToWrite > 0 else { return }
        state = .downloading(Double(totalBytesWritten) / Double(totalBytesExpectedToWrite))
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        task = nil
        session.finishTasksAndInvalidate()
        self.session = nil
        if let status = (downloadTask.response as? HTTPURLResponse)?.statusCode, status != 200 {
            state = .failed("Сервер ответил ошибкой \(status) — попробуйте позже")
            return
        }
        // файл по `location` живёт только до выхода из этого метода — забираем его себе,
        // а проверку и распаковку (секунды) делаем в фоне: главный поток — это перехват клавиатуры
        let owned = FileManager.default.temporaryDirectory.appendingPathComponent("vallo-model-\(UUID().uuidString).zip")
        do {
            try FileManager.default.moveItem(at: location, to: owned)
        } catch {
            state = .failed(error.localizedDescription)
            return
        }
        state = .downloading(1)
        DispatchQueue.global(qos: .utility).async { [self] in
            defer { try? FileManager.default.removeItem(at: owned) }
            let result: State
            do {
                try install(zip: owned)
                result = .ready
            } catch {
                result = .failed(error.localizedDescription)
            }
            DispatchQueue.main.async { [self] in
                state = result
                if result == .ready { onChange?() }
            }
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        guard let error, (error as NSError).code != NSURLErrorCancelled else { return }
        state = .failed("Не скачалось: \(error.localizedDescription)")
        self.task = nil
    }

    // MARK: - Установка

    struct InstallError: LocalizedError {
        let errorDescription: String?
    }

    /// Сверить контрольную сумму, распаковать во временную папку и только потом заменить модель.
    private func install(zip: URL) throws {
        // контрольная сумма по кускам, без загрузки 228 МБ в память целиком
        let handle = try FileHandle(forReadingFrom: zip)
        defer { try? handle.close() }
        var hasher = SHA256()
        while let chunk = try handle.read(upToCount: 4 << 20), !chunk.isEmpty { hasher.update(data: chunk) }
        let hash = hasher.finalize().map { String(format: "%02x", $0) }.joined()
        guard hash == ModelStore.sha256 else {
            throw InstallError(errorDescription: "Файл скачался с ошибкой — попробуйте ещё раз")
        }
        let fm = FileManager.default
        let models = CommaService.modelDirectory.deletingLastPathComponent()
        try fm.createDirectory(at: models, withIntermediateDirectories: true)
        let temp = models.appendingPathComponent(".unpack-\(UUID().uuidString)")
        defer { try? fm.removeItem(at: temp) }
        let ditto = Process()
        ditto.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
        ditto.arguments = ["-x", "-k", zip.path, temp.path]
        try ditto.run()
        ditto.waitUntilExit()
        let unpacked = temp.appendingPathComponent(CommaService.modelName)
        guard ditto.terminationStatus == 0,
              fm.fileExists(atPath: unpacked.appendingPathComponent("decoder_model.onnx").path) else {
            throw InstallError(errorDescription: "Не получилось распаковать модель")
        }
        try? fm.removeItem(at: CommaService.modelDirectory)
        try fm.moveItem(at: unpacked, to: CommaService.modelDirectory)
    }
}
