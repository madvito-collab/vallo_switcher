import AppKit
import Network
import ValloCore

/// Связь с плагином Vallo в Фигме: локальный WebSocket-сервер только на 127.0.0.1.
/// Плагин в каждом открытом файле подключается сам и говорит «hello» с именем файла.
/// Протокол — figma-plugin/PROTOCOL.md.
final class FigmaBridge {
    static let port: NWEndpoint.Port = 48731

    private let store: SettingsStore
    private var listeners: [NWListener] = []
    /// Подключения по имени файла (последнее подключение файла побеждает).
    private var byFile: [String: NWConnection] = [:]
    /// Сообщение от плагина (на главном потоке).
    var onMessage: ((FigmaMessage, _ file: String) -> Void)?

    init(store: SettingsStore) { self.store = store }

    var connectedFiles: [String] { Array(byFile.keys) }

    /// Страница плагина в Figma Community (опубликован автором 2026-11-05).
    static let communityURL = URL(string: "https://www.figma.com/community/plugin/1700095930867895220")!

    /// Копия плагина вне приложения: в окне выбора файла Фигмы внутрь .app не зайти.
    /// Перезаписывается при каждом запуске — после обновления свитчера Фигма подхватит новый плагин.
    static var pluginFolder: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Vallo Switcher/Figma Plugin", isDirectory: true)
    }

    static func installPluginFiles() {
        guard let source = Bundle.main.resourceURL?.appendingPathComponent("figma-plugin", isDirectory: true) else { return }
        let fm = FileManager.default
        do {
            try fm.createDirectory(at: pluginFolder, withIntermediateDirectories: true)
            for name in ["manifest.json", "code.js", "ui.html"] {
                let to = pluginFolder.appendingPathComponent(name)
                try? fm.removeItem(at: to)
                try fm.copyItem(at: source.appendingPathComponent(name), to: to)
            }
        } catch {
            log.debug("figma: cannot copy plugin: \(error.localizedDescription)")
        }
    }

    static func revealPlugin() {
        installPluginFiles()
        NSWorkspace.shared.activateFileViewerSelecting([pluginFolder.appendingPathComponent("manifest.json")])
    }

    func start() {
        FigmaBridge.installPluginFiles()
        let ws = NWProtocolWebSocket.Options()
        ws.autoReplyPing = true
        // окно плагина приходит с Origin «null»; обычная веб-страница — со своим адресом: такую не пускаем
        ws.setClientRequestHandler(.main) { _, headers in
            let origin = headers.first { $0.name.lowercased() == "origin" }?.value
            let ok = origin == nil || origin == "null"
            if !ok { log.debug("figma: rejected origin") }
            return NWProtocolWebSocket.Response(status: ok ? .accept : .reject, subprotocol: nil)
        }
        // «localhost» в окне плагина может оказаться и 127.0.0.1, и ::1 — слушаем оба, наружу — ничего
        listeners = ["127.0.0.1", "::1"].compactMap { host in
            let params = NWParameters.tcp
            params.defaultProtocolStack.applicationProtocols.insert(ws, at: 0)
            params.requiredLocalEndpoint = NWEndpoint.hostPort(host: NWEndpoint.Host(host), port: FigmaBridge.port)
            params.allowLocalEndpointReuse = true
            do {
                let listener = try NWListener(using: params)
                listener.newConnectionHandler = { [weak self] in self?.accept($0) }
                listener.stateUpdateHandler = { state in
                    if case .failed(let error) = state { log.debug("figma: listener \(host) failed \(error.localizedDescription)") }
                }
                listener.start(queue: .main)
                return listener
            } catch {
                // порт занят (второй экземпляр?) — без Фигмы, но без падения
                log.debug("figma: cannot listen on \(host):\(FigmaBridge.port.rawValue): \(error.localizedDescription)")
                return nil
            }
        }
    }

    @discardableResult
    func send(_ message: FigmaMessage, toFile file: String) -> Bool {
        guard let connection = byFile[file] else { return false }
        let meta = NWProtocolWebSocket.Metadata(opcode: .text)
        let context = NWConnection.ContentContext(identifier: "figma", metadata: [meta])
        connection.send(content: message.encode(), contentContext: context, isComplete: true, completion: .idempotent)
        return true
    }

    private func accept(_ connection: NWConnection) {
        connection.stateUpdateHandler = { [weak self, weak connection] state in
            switch state {
            case .failed, .cancelled:
                guard let self, let connection else { return }
                self.forget(connection)
            default: break
            }
        }
        connection.start(queue: .main)
        receive(on: connection, file: nil)
    }

    private func receive(on connection: NWConnection, file: String?) {
        connection.receiveMessage { [weak self] data, context, isComplete, error in
            guard let self else { return }
            // плагин закрылся («Готово» → closePlugin): ошибка, кадр close или конец потока без данных —
            // соединение забываем сразу, иначе следующее нажатие ушло бы в мёртвый сокет
            let meta = context?.protocolMetadata(definition: NWProtocolWebSocket.definition) as? NWProtocolWebSocket.Metadata
            if error != nil || meta?.opcode == .close || (data == nil && isComplete) {
                self.forget(connection)
                connection.cancel()
                return
            }
            var file = file
            if let data, let message = FigmaMessage.decode(data) {
                if case let .hello(_, name, _) = message {
                    file = name
                    self.byFile[name]?.cancel()
                    self.byFile[name] = connection
                    if !self.store.figmaPluginSeen { self.store.figmaPluginSeen = true }
                    log.debug("figma: plugin connected (\(self.byFile.count) files)")
                }
                if let file { self.onMessage?(message, file) }
            }
            self.receive(on: connection, file: file)
        }
    }

    private func forget(_ connection: NWConnection) {
        for (file, c) in byFile where c === connection { byFile[file] = nil }
    }
}
