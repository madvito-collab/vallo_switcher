import AppKit
import Combine
import ValloCore
import WebKit

/// Страница окна настроек. id совпадают со страницей (`settings-ui/src/bridge.ts`).
enum SettingsPage: String {
    case general, layout, correct, figma, help, about
}

/// Окно настроек: веб-страница (`Support/SettingsUI/index.html`, исходники — `settings-ui/`) в обычном окне.
/// Страница ничего не хранит: показывает состояние свитчера и шлёт изменения.
/// Протокол — `docs/superpowers/specs/2026-11-05-web-settings-design.md`.
final class SettingsWindow: NSObject, WKScriptMessageHandler, WKNavigationDelegate, NSWindowDelegate {
    let model: SettingsModel
    let modelStore: ModelStore
    private var window: NSWindow?
    private var web: WKWebView?
    private var ready = false
    /// Страница, на которой открыть окно (уходит в следующем состоянии один раз).
    private var pendingPage: SettingsPage?
    private var watch: Set<AnyCancellable> = []
    private var pushScheduled = false
    private let recorder = HotkeyRecorder()
    private var recording: SettingsModel.Action?
    private var macAutocorrect = macAutocorrectEnabled()
    private var iconCache: [String: String] = [:]

    init(model: SettingsModel, modelStore: ModelStore) {
        self.model = model
        self.modelStore = modelStore
        super.init()
        // любое изменение модели или загрузки — новое состояние на страницу (одним пакетом за проход цикла)
        model.objectWillChange.sink { [weak self] in self?.schedulePush() }.store(in: &watch)
        modelStore.objectWillChange.sink { [weak self] in self?.schedulePush() }.store(in: &watch)
        // настройки клавиатуры правят в другом окне — сверяемся, когда свитчер снова спереди
        NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)
            .sink { [weak self] _ in
                self?.macAutocorrect = macAutocorrectEnabled()
                self?.schedulePush()
            }
            .store(in: &watch)
    }

    func show(page: SettingsPage? = nil) {
        model.reload()
        // пока открыты настройки — значок в Dock, как у обычной программы; закрыли — снова только у часов
        NSApp.setActivationPolicy(.regular)
        if window == nil { window = makeWindow() }
        pendingPage = page
        schedulePush()
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    private func makeWindow() -> NSWindow {
        let config = WKWebViewConfiguration()
        config.userContentController.add(self, name: "vallo")
        let web = WKWebView(frame: .zero, configuration: config)
        web.navigationDelegate = self
        // фон страницы — до её загрузки окно не мигает белым в тёмной теме
        web.underPageBackgroundColor = .windowBackgroundColor
        if let url = Bundle.main.url(forResource: "index", withExtension: "html", subdirectory: "SettingsUI") {
            web.loadFileURL(url, allowingReadAccessTo: url.deletingLastPathComponent())
        }
        self.web = web

        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 900, height: 640),
                         styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                         backing: .buffered, defer: false)
        // заголовок не рисуем: «Настройки» — в боковом меню страницы, кнопки окна — поверх него
        w.titleVisibility = .hidden
        w.titlebarAppearsTransparent = true
        w.title = "Настройки Vallo Switcher"
        // страница во всё окно забирает мышь — окно не таскалось. Сверху прозрачная полоса для перетаскивания
        // (кнопки окна лежат выше неё и нажимаются как обычно); на странице там пусто
        let container = NSView()
        web.frame = container.bounds
        web.autoresizingMask = [.width, .height]
        container.addSubview(web)
        let drag = DragStrip(frame: NSRect(x: 0, y: container.bounds.height - DragStrip.height,
                                           width: container.bounds.width, height: DragStrip.height))
        drag.autoresizingMask = [.width, .minYMargin]
        container.addSubview(drag)
        w.contentView = container
        w.contentMinSize = NSSize(width: 760, height: 480)
        w.isReleasedWhenClosed = false
        w.delegate = self
        w.center()
        w.setFrameAutosaveName("SettingsWeb")
        return w
    }

    func windowWillClose(_ notification: Notification) {
        stopRecording()
        NSApp.setActivationPolicy(.accessory)
    }

    // MARK: - Страница → свитчер

    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let body = message.body as? [String: Any], let type = body["type"] as? String else { return }
        switch type {
        case "ready":
            ready = true
            push()
        case "set":
            guard let key = body["key"] as? String else { return }
            set(key, body["value"])
        case "action":
            guard let name = body["name"] as? String else { return }
            perform(name, body["arg"])
        default:
            break
        }
    }

    private func set(_ key: String, _ value: Any?) {
        let flag = value as? Bool ?? false
        let list = value as? [String] ?? []
        switch key {
        case "launchAtLogin": model.launchAtLogin = flag
        case "debugMode": model.debugMode = flag
        case "autoSwitch": model.autoSwitch = flag
        case "typography": model.typographyLive = flag
        case "typos": model.typoLive = flag
        case "yo": model.yo = flag
        case "capitalize": model.typoCapitalize = flag
        case "emoji.preset":
            if let preset = value as? String, ["millennial", "zoomer", "custom", "off"].contains(preset) { model.emojiPreset = preset }
        case "emoji.custom":
            // со страницы приходит весь список; пары держим как есть — пустые поля человек ещё заполняет
            let pairs = (value as? [[String: Any]] ?? []).map {
                ["trigger": $0["trigger"] as? String ?? "", "emoji": $0["emoji"] as? String ?? ""]
            }
            model.emojiCustom = pairs
        case "autoUpdate": model.autoUpdate = flag
        case "hotkey.convert": chooseHotkey(value as? String, for: .convert)
        case "hotkey.correct": chooseHotkey(value as? String, for: .typography)
        case "apps.layout": model.excluded = list
        case "apps.correct": model.typographyExcluded = list
        case "words.layout": model.learned = list
        case "words.correct": model.typoWords = list
        default: log.debug("settings: unknown key \(key)")
        }
        schedulePush()
    }

    private func perform(_ name: String, _ arg: Any?) {
        switch name {
        case "pickApps":
            let keyPath: ReferenceWritableKeyPath<SettingsModel, [String]> = (arg as? String) == "correct" ? \.typographyExcluded : \.excluded
            for id in model.pickApps() where !model[keyPath: keyPath].contains(id) { model[keyPath: keyPath].append(id) }
        case "addWord":
            guard let a = arg as? [String: Any], let word = a["word"] as? String else { return }
            if a["list"] as? String == "correct" { model.addTypoWord(word) } else { model.addLearnedWord(word) }
        case "checkUpdates": model.checkForUpdates()
        case "model.download": modelStore.download()
        case "model.cancel": modelStore.cancel()
        case "model.delete": modelStore.delete()
        case "openKeyboardSettings":
            NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.Keyboard-Settings.extension")!)
        case "openFigmaPlugin": NSWorkspace.shared.open(FigmaBridge.communityURL)
        case "openGitHub": NSWorkspace.shared.open(URL(string: "https://github.com/madvito-collab/vallo_switcher/issues")!)
        case "openFullHelp":
            if let url = Bundle.main.url(forResource: "Help", withExtension: "html") { NSWorkspace.shared.open(url) }
        case "cancelRecording": stopRecording()
        // системная панель смайликов — вставит выбранный в поле «Другой смайлик», где сейчас курсор
        case "emojiPanel": NSApp.orderFrontCharacterPalette(nil)
        default: log.debug("settings: unknown action \(name)")
        }
        schedulePush()
    }

    /// Вариант из списка сочетаний: двойной модификатор, «Своё сочетание…» (запись), «Выключено».
    private func chooseHotkey(_ id: String?, for action: SettingsModel.Action) {
        guard let id else { return }
        model.hotkeyMessage = nil
        if action == .typography { model.typographyHotkey = id != "off" }
        if id == "record" {
            recording = action
            recorder.start { [weak self] hotkey in
                guard let self else { return }
                self.recording = nil
                if let hotkey { self.model.setHotkey(hotkey, for: action) }
                self.schedulePush()
            }
        } else if id.hasPrefix("double:"), let key = ModifierKey(rawValue: String(id.dropFirst(7))) {
            model.setHotkey(.doubleTap(key), for: action)
        }
    }

    private func stopRecording() {
        recorder.stop()
        recording = nil
        schedulePush()
    }

    // MARK: - Свитчер → страница

    private func schedulePush() {
        guard !pushScheduled else { return }
        pushScheduled = true
        // objectWillChange приходит до изменения — шлём на следующем проходе, когда значения уже новые
        DispatchQueue.main.async { [weak self] in
            self?.pushScheduled = false
            self?.push()
        }
    }

    private func push() {
        guard ready, let web,
              let data = try? JSONSerialization.data(withJSONObject: state()),
              let json = String(data: data, encoding: .utf8) else { return }
        pendingPage = nil
        web.evaluateJavaScript("window.vallo && window.vallo.receive(\(json))")
    }

    private func state() -> [String: Any] {
        let modelState: [String: Any]
        switch modelStore.state {
        case .notDownloaded: modelState = ["state": "notDownloaded", "progress": 0, "message": ""]
        case .downloading(let p): modelState = ["state": "downloading", "progress": p, "message": ""]
        case .ready: modelState = ["state": "ready", "progress": 1, "message": ""]
        case .failed(let message): modelState = ["state": "failed", "progress": 0, "message": message]
        }
        let modelMissing: Bool = {
            switch modelStore.state {
            case .notDownloaded, .failed: return true
            default: return false
            }
        }()
        var hotkeyError: Any = NSNull()
        if let text = model.hotkeyMessage, let action = model.hotkeyMessageAction {
            hotkeyError = ["action": action == .convert ? "convert" : "correct", "text": text]
        }
        let iso = ISO8601DateFormatter()
        return [
            "platform": "mac",
            "version": model.version,
            "page": pendingPage?.rawValue ?? NSNull(),
            "general": ["launchAtLogin": model.launchAtLogin, "debugMode": model.debugMode],
            "attention": ["modelMissing": modelMissing, "macAutocorrect": macAutocorrect],
            "layout": [
                "autoSwitch": model.autoSwitch,
                "hotkey": hotkeyID(model.hotkey(.convert)),
                "hotkeyTitle": model.hotkey(.convert).title,
                "apps": apps(model.excluded),
                "words": model.learned,
            ],
            "correct": [
                "available": true,
                "hotkey": model.typographyHotkey ? hotkeyID(model.hotkey(.typography)) : "off",
                "hotkeyTitle": model.hotkey(.typography).title,
                "capitalize": model.typoCapitalize,
                "typography": model.typographyLive,
                "typos": model.typoLive,
                "yo": model.yo,
                "apps": apps(model.typographyExcluded),
                "words": model.typoWords,
                "emoji": ["preset": model.emojiPreset, "custom": model.emojiCustom],
            ],
            "hotkeyOptions": ModifierKey.allCases.map { ["id": "double:\($0.rawValue)", "title": $0.title] },
            "canRecordHotkey": true,
            "recording": recording.map { $0 == .convert ? "convert" : "correct" } ?? NSNull(),
            "hotkeyError": hotkeyError,
            "integrations": ["figma": true, "figmaPublished": FigmaBridge.communityPublished],
            "about": [
                "updates": true,
                "autoUpdate": model.autoUpdate,
                "lastCheck": model.lastUpdateCheck.map { iso.string(from: $0) } ?? NSNull(),
                "model": modelState.merging(["size": ModelStore.downloadSize, "disk": ModelStore.diskSize]) { a, _ in a },
            ],
        ]
    }

    private func hotkeyID(_ hotkey: Hotkey) -> String {
        if case .doubleTap(let key) = hotkey { return "double:\(key.rawValue)" }
        return "custom"
    }

    /// Установленные приложения из списка — с именем и значком.
    private func apps(_ ids: [String]) -> [[String: String]] {
        model.installed(ids).map { id in
            var entry = ["id": id, "name": model.appName(id)]
            if let icon = icon(id) { entry["icon"] = icon }
            return entry
        }
    }

    private func icon(_ id: String) -> String? {
        if let cached = iconCache[id] { return cached }
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: id) else { return nil }
        let image = NSWorkspace.shared.icon(forFile: url.path)
        let size = NSSize(width: 48, height: 48)   // 24 точки на экранах Retina
        guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(size.width), pixelsHigh: Int(size.height),
                                         bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                         colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { return nil }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
        image.draw(in: NSRect(origin: .zero, size: size))
        NSGraphicsContext.restoreGraphicsState()
        guard let png = rep.representation(using: .png, properties: [:]) else { return nil }
        let uri = "data:image/png;base64," + png.base64EncodedString()
        iconCache[id] = uri
        return uri
    }

    // MARK: - Ссылки со страницы — в браузер, не внутрь окна

    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction,
                 decisionHandler: @escaping @MainActor (WKNavigationActionPolicy) -> Void) {
        if let url = action.request.url, !url.isFileURL {
            NSWorkspace.shared.open(url)
            decisionHandler(.cancel)
        } else {
            decisionHandler(.allow)
        }
    }
}

/// Полоса над страницей, за которую таскают окно; двойной щелчок — развернуть, как у заголовка окна.
private final class DragStrip: NSView {
    static let height: CGFloat = 32

    override func mouseDown(with event: NSEvent) {
        if event.clickCount == 2 {
            window?.performZoom(nil)
        } else {
            window?.performDrag(with: event)
        }
    }
}
