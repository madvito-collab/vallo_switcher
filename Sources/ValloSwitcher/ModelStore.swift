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
        // файл по `location` живёт только до выхода из этого метода — проверяем и распаковываем сразу
        do {
            try install(zip: location)
            state = .ready
            onChange?()
        } catch {
            state = .failed(error.localizedDescription)
        }
        task = nil
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
        let data = try Data(contentsOf: zip, options: .mappedIfSafe)
        let hash = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
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
