import AppKit
import ServiceManagement
import SwiftUI
import ValloCore

/// Модель окна настроек поверх SettingsStore.
final class SettingsModel: ObservableObject {
    private let store: SettingsStore
    /// Список запомненных слов поменялся — движку надо обновить судью.
    var onLearnedChanged: (([String]) -> Void)?
    /// Сочетания поменялись — перехвату надо перечитать.
    var onHotkeysChanged: (() -> Void)?

    @Published var autoSwitch: Bool { didSet { store.autoSwitchEnabled = autoSwitch } }
    @Published var debugMode: Bool {
        didSet {
            store.debugMode = debugMode
            DebugLog.enabled = debugMode
        }
    }
    @Published var excluded: [String] { didSet { store.excludedBundleIDs = excluded } }
    @Published var typographyLive: Bool { didSet { store.typographyLiveEnabled = typographyLive } }
    @Published var typographyHotkey: Bool { didSet { store.typographyHotkeyEnabled = typographyHotkey } }
    @Published var typographyExcluded: [String] { didSet { store.typographyExcludedBundleIDs = typographyExcluded } }
    @Published private(set) var hotkeyConvert: Hotkey
    @Published private(set) var hotkeyTypography: Hotkey
    /// Сообщение под сочетаниями: конфликт или «нажми сочетание».
    @Published var hotkeyMessage: String?
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
        typographyLive = store.typographyLiveEnabled
        typographyHotkey = store.typographyHotkeyEnabled
        typographyExcluded = store.typographyExcludedBundleIDs
        hotkeyConvert = store.hotkeyConvert
        hotkeyTypography = store.hotkeyTypography
        learned = store.learnedWords
        launchAtLogin = SMAppService.mainApp.status == .enabled
    }

    /// Перечитать из хранилища (слова учатся, пока окно открыто).
    func reload() {
        autoSwitch = store.autoSwitchEnabled
        if learned != store.learnedWords { learned = store.learnedWords }
    }

    /// Выбрать приложения в «Программах». Возвращает их bundle ID.
    func pickApps() -> [String] {
        let panel = NSOpenPanel()
        panel.directoryURL = URL(fileURLWithPath: "/Applications")
        panel.allowedContentTypes = [.application]
        panel.allowsMultipleSelection = true
        guard panel.runModal() == .OK else { return [] }
        return panel.urls.compactMap { Bundle(url: $0)?.bundleIdentifier }
    }

    enum Action { case convert, typography }

    func hotkey(_ action: Action) -> Hotkey { action == .convert ? hotkeyConvert : hotkeyTypography }

    /// Назначить сочетание. Занято другим действием — не сохраняем и объясняем.
    func setHotkey(_ hotkey: Hotkey, for action: Action) {
        let other = action == .convert ? hotkeyTypography : hotkeyConvert
        guard hotkey != other else {
            hotkeyMessage = "Это сочетание уже занято: \(hotkey.title)"
            return
        }
        hotkeyMessage = nil
        if action == .convert {
            hotkeyConvert = hotkey
            store.hotkeyConvert = hotkey
        } else {
            hotkeyTypography = hotkey
            store.hotkeyTypography = hotkey
        }
        onHotkeysChanged?()
    }

    func resetHotkeys() {
        hotkeyMessage = nil
        hotkeyConvert = .defaultConvert
        hotkeyTypography = .defaultTypography
        store.hotkeyConvert = .defaultConvert
        store.hotkeyTypography = .defaultTypography
        onHotkeysChanged?()
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
        TabView {
            GeneralTab(model: model).tabItem { Label("Основное", systemImage: "gearshape") }
            AppListTab(title: "Здесь автопереключение молчит. Двойной Shift работает.",
                       apps: $model.excluded, model: model)
                .tabItem { Label("Исключения", systemImage: "nosign") }
            TypographyTab(model: model).tabItem { Label("Типографика", systemImage: "textformat") }
            HotkeysTab(model: model).tabItem { Label("Сочетания", systemImage: "keyboard") }
            LearnedTab(model: model).tabItem { Label("Слова", systemImage: "text.book.closed") }
        }
        .frame(width: 500, height: 440)
    }
}

private struct GeneralTab: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        Form {
            Section {
                Toggle("Автопереключение", isOn: $model.autoSwitch)
                Toggle("Запускать вместе с Маком", isOn: $model.launchAtLogin)
                Toggle("Режим отладки", isOn: $model.debugMode)
            } footer: {
                Text("Смена раскладки по сочетанию (\(model.hotkey(.convert).title)) работает всегда, даже когда автопереключение выключено.")
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }
}

/// Список приложений с кнопками «Убрать» и «Добавить…».
private struct AppListTab: View {
    let title: String
    @Binding var apps: [String]
    @ObservedObject var model: SettingsModel

    var body: some View {
        Form {
            Section {
                ForEach(apps, id: \.self) { id in
                    HStack {
                        Text(model.appName(id))
                        Spacer()
                        Button("Убрать") { apps.removeAll { $0 == id } }
                    }
                }
                Button("Добавить приложение…") {
                    for id in model.pickApps() where !apps.contains(id) { apps.append(id) }
                }
            } footer: {
                Text(title).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }
}

private struct TypographyTab: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        Form {
            Section {
                Toggle("Типографика на лету", isOn: $model.typographyLive)
                Toggle("Причёсывать выделенное по сочетанию (\(model.hotkey(.typography).title))", isOn: $model.typographyHotkey)
            } footer: {
                Text("Тире, «ёлочки», неразрывные пробелы, ×, …, ©. Backspace сразу после замены возвращает набранное.")
                    .foregroundStyle(.secondary)
            }
            Section("Где типографика на лету молчит") {
                ForEach(model.typographyExcluded, id: \.self) { id in
                    HStack {
                        Text(model.appName(id))
                        Spacer()
                        Button("Убрать") { model.typographyExcluded.removeAll { $0 == id } }
                    }
                }
                Button("Добавить приложение…") {
                    for id in model.pickApps() where !model.typographyExcluded.contains(id) { model.typographyExcluded.append(id) }
                }
                Text("Редакторы JetBrains исключены всегда.").foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }
}

private struct HotkeysTab: View {
    @ObservedObject var model: SettingsModel
    @State private var recorder = HotkeyRecorder()
    @State private var recording: SettingsModel.Action?

    var body: some View {
        Form {
            Section {
                row("Сменить раскладку", .convert)
                row("Причесать типографику", .typography)
            } footer: {
                if let message = model.hotkeyMessage {
                    Text(message).foregroundStyle(.red)
                } else if recording != nil {
                    Text("Нажми сочетание с ⌘, ⌥ или ⌃. Esc — отмена.").foregroundStyle(.secondary)
                }
            }
            Section {
                Button("По умолчанию") {
                    recorder.stop()
                    recording = nil
                    model.resetHotkeys()
                }
            }
        }
        .formStyle(.grouped)
        .onDisappear { recorder.stop() }
    }

    private enum Choice: Hashable { case modifier(ModifierKey), custom }

    private func row(_ title: String, _ action: SettingsModel.Action) -> some View {
        let current = model.hotkey(action)
        let selection = Binding<Choice>(
            get: {
                if case .doubleTap(let key) = current { return .modifier(key) }
                return .custom
            },
            set: { choice in
                switch choice {
                case .modifier(let key):
                    model.setHotkey(.doubleTap(key), for: action)
                case .custom:
                    record(action)
                }
            })
        return Picker(title, selection: selection) {
            ForEach(ModifierKey.allCases, id: \.self) { Text($0.title).tag(Choice.modifier($0)) }
            Divider()
            if case .combo = current {
                Text(recording == action ? "Нажми сочетание…" : current.title).tag(Choice.custom)
            } else {
                Text(recording == action ? "Нажми сочетание…" : "Свой вариант…").tag(Choice.custom)
            }
        }
    }

    private func record(_ action: SettingsModel.Action) {
        model.hotkeyMessage = nil
        recording = action
        recorder.start { hotkey in
            recording = nil
            if let hotkey { model.setHotkey(hotkey, for: action) }
        }
    }
}

private struct LearnedTab: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        Form {
            Section {
                if model.learned.isEmpty {
                    Text("Пока пусто. Слово попадает сюда, когда ты дважды откатываешь его.")
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
            } footer: {
                Text("Эти слова свитчер сам не трогает.").foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
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
