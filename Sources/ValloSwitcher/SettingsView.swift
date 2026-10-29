import AppKit
import ServiceManagement
import SwiftUI

/// Модель окна настроек поверх SettingsStore.
final class SettingsModel: ObservableObject {
    private let store: SettingsStore
    /// Список запомненных слов поменялся — движку надо обновить судью.
    var onLearnedChanged: (([String]) -> Void)?

    @Published var autoSwitch: Bool { didSet { store.autoSwitchEnabled = autoSwitch } }
    @Published var debugMode: Bool {
        didSet {
            store.debugMode = debugMode
            DebugLog.enabled = debugMode
        }
    }
    @Published var excluded: [String] { didSet { store.excludedBundleIDs = excluded } }
    @Published var learned: [String] {
        didSet {
            store.learnedWords = learned
            onLearnedChanged?(learned)
        }
    }
    @Published var launchAtLogin: Bool {
        didSet {
            guard launchAtLogin != (SMAppService.mainApp.status == .enabled) else { return }
            do {
                if launchAtLogin { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            } catch {
                log.debug("launch at login: \(error.localizedDescription)")
            }
        }
    }

    init(store: SettingsStore) {
        self.store = store
        autoSwitch = store.autoSwitchEnabled
        debugMode = store.debugMode
        excluded = store.excludedBundleIDs
        learned = store.learnedWords
        launchAtLogin = SMAppService.mainApp.status == .enabled
    }

    /// Перечитать из хранилища (слова учатся, пока окно открыто).
    func reload() {
        autoSwitch = store.autoSwitchEnabled
        if learned != store.learnedWords { learned = store.learnedWords }
    }

    func addApp() {
        let panel = NSOpenPanel()
        panel.directoryURL = URL(fileURLWithPath: "/Applications")
        panel.allowedContentTypes = [.application]
        panel.allowsMultipleSelection = true
        guard panel.runModal() == .OK else { return }
        for url in panel.urls {
            if let id = Bundle(url: url)?.bundleIdentifier, !excluded.contains(id) { excluded.append(id) }
        }
    }

    /// Имя приложения по bundle ID — для списка.
    func appName(_ id: String) -> String {
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: id) else { return id }
        return FileManager.default.displayName(atPath: url.path).replacingOccurrences(of: ".app", with: "")
    }
}

struct SettingsView: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        Form {
            Section {
                Toggle("Автопереключение", isOn: $model.autoSwitch)
                Toggle("Запускать вместе с Маком", isOn: $model.launchAtLogin)
                Toggle("Режим отладки", isOn: $model.debugMode)
            } footer: {
                Text("Двойной левый Shift работает всегда, даже когда автопереключение выключено.")
                    .foregroundStyle(.secondary)
            }

            Section("Приложения-исключения") {
                ForEach(model.excluded, id: \.self) { id in
                    HStack {
                        Text(model.appName(id))
                        Spacer()
                        Button("Убрать") { model.excluded.removeAll { $0 == id } }
                    }
                }
                Button("Добавить приложение…") { model.addApp() }
            }

            Section("Запомненные слова") {
                if model.learned.isEmpty {
                    Text("Пока пусто. Слово попадает сюда, когда ты дважды откатываешь его двойным Shift.")
                        .foregroundStyle(.secondary)
                }
                ForEach(model.learned, id: \.self) { word in
                    HStack {
                        Text(word).font(.body.monospaced())
                        Spacer()
                        Button("Удалить") { model.learned.removeAll { $0 == word } }
                    }
                }
                if !model.learned.isEmpty {
                    Button("Очистить всё", role: .destructive) { model.learned.removeAll() }
                }
            }
        }
        .formStyle(.grouped)
        .frame(width: 440, height: 520)
    }
}

/// Окно настроек: одно на всё приложение.
final class SettingsWindow {
    private var window: NSWindow?
    let model: SettingsModel

    init(model: SettingsModel) { self.model = model }

    func show() {
        model.reload()
        if window == nil {
            let w = NSWindow(contentViewController: NSHostingController(rootView: SettingsView(model: model)))
            w.title = "Vallo Switcher"
            w.styleMask = [.titled, .closable]
            w.isReleasedWhenClosed = false
            w.center()
            window = w
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }
}
