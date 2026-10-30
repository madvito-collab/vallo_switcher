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
        let known = ["com.googlecode.iterm2": "iTerm", "com.todesktop.230313mzl4w4u92": "Cursor",
                     "com.apple.Terminal": "Терминал", "com.microsoft.VSCode": "Visual Studio Code", "com.apple.dt.Xcode": "Xcode"]
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: id) else { return known[id] ?? id }
        return FileManager.default.displayName(atPath: url.path).replacingOccurrences(of: ".app", with: "")
    }
}

/// Тексты в окне — через нашу же типографику: неразрывные пробелы, «ёлочки», тире.
private func T(_ s: String) -> String { Typographer.format(s) }

/// Подсказка под разделом — слева, как в системных настройках.
private struct Hint: View {
    let text: String
    init(_ text: String) { self.text = text }
    var body: some View {
        Text(T(text)).foregroundStyle(.secondary).frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// Нативная круглая кнопка «?» — открывает справку свитчера.
private struct HelpButton: NSViewRepresentable {
    func makeNSView(context: Context) -> NSButton {
        let b = NSButton(title: "", target: context.coordinator, action: #selector(Coordinator.open))
        b.bezelStyle = .helpButton
        return b
    }
    func updateNSView(_ nsView: NSButton, context: Context) {}
    func makeCoordinator() -> Coordinator { Coordinator() }
    final class Coordinator: NSObject {
        @objc func open() {
            if let url = Bundle.main.url(forResource: "Help", withExtension: "html") { NSWorkspace.shared.open(url) }
        }
    }
}

/// Под серым блоком: подсказка слева, кнопки справа — как «Добавить аккаунт…» в системных настройках.
private struct BelowBlock<Buttons: View>: View {
    let hint: String?
    @ViewBuilder let buttons: Buttons
    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            if let hint { Hint(hint) } else { Spacer() }
            buttons
        }
    }
}

/// Переключатель с подзаголовком.
private struct SubtitledToggle: View {
    let title: String
    let subtitle: String
    @Binding var isOn: Bool
    var body: some View {
        Toggle(isOn: $isOn) {
            VStack(alignment: .leading, spacing: 2) {
                Text(T(title))
                Text(T(subtitle)).font(.callout).foregroundStyle(.secondary)
            }
        }
    }
}

struct GeneralTab: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        Form {
            Section {
                Toggle("Автопереключение", isOn: $model.autoSwitch)
                Toggle("Запускать вместе с Маком", isOn: $model.launchAtLogin)
                Toggle(T("Режим отладки"), isOn: $model.debugMode)
            } footer: {
                BelowBlock(hint: "\(model.hotkey(.convert).title) переводит набранное или выделенное в другую раскладку всегда — даже когда автопереключение выключено.") {
                    HelpButton()
                }
            }
        }
        .formStyle(.grouped)
    }
}

/// Строки приложений с кнопкой «Убрать».
private struct AppRows: View {
    @Binding var apps: [String]
    @ObservedObject var model: SettingsModel

    var body: some View {
        if apps.isEmpty {
            Text("Нет приложений").foregroundStyle(.secondary)
        }
        ForEach(apps, id: \.self) { id in
            HStack {
                Text(model.appName(id))
                Spacer()
                Button("Убрать") { apps.removeAll { $0 == id } }
            }
        }
    }
}

/// Все исключения в одном месте: автопереключение и типографика.
struct ExclusionsTab: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        Form {
            Section {
                AppRows(apps: $model.excluded, model: model)
            } header: {
                Text("Автопереключение")
            } footer: {
                BelowBlock(hint: "Здесь автопереключение молчит. Смена раскладки по сочетанию работает.") {
                    Button("Добавить приложение…") { add(to: \.excluded) }
                }
            }
            Section {
                AppRows(apps: $model.typographyExcluded, model: model)
            } header: {
                Text("Типографика на лету")
            } footer: {
                BelowBlock(hint: "Здесь тире и кавычки не ставятся. Редакторы JetBrains исключены всегда.") {
                    Button("Добавить приложение…") { add(to: \.typographyExcluded) }
                }
            }
        }
        .formStyle(.grouped)
    }

    private func add(to list: ReferenceWritableKeyPath<SettingsModel, [String]>) {
        for id in model.pickApps() where !model[keyPath: list].contains(id) { model[keyPath: list].append(id) }
    }
}

struct TypographyTab: View {
    @ObservedObject var model: SettingsModel

    /// Что исправляется — примеры «было → стало».
    private let examples: [(String, String)] = [
        ("Москва - столица", "Москва — столица"),
        ("\"текст\"", "«текст»"),
        ("ну...", "ну…"),
        ("5 - 3 · 5x3", "5 − 3 · 5×3"),
        ("1990-2000", "1990–2000"),
        ("(c) · +- · ->", "© · ± · →"),
        ("в доме", "в доме без переноса после «в»"),
    ]

    var body: some View {
        Form {
            Section {
                Toggle("Типографика на лету", isOn: $model.typographyLive)
                SubtitledToggle(title: "Причёсывать выделенное",
                                subtitle: "По сочетанию \(model.hotkey(.typography).title)",
                                isOn: $model.typographyHotkey)
            }
            Section("Что исправляется") {
                ForEach(examples, id: \.0) { before, after in
                    LabeledContent(before) { Text(after) }
                }
            }
            Section {} footer: {
                BelowBlock(hint: "Передумал — нажми Backspace сразу после замены, и вернётся набранное. Где типографика не работает — на вкладке «Исключения».") {
                    HelpButton()
                }
            }
        }
        .formStyle(.grouped)
    }
}

/// Поле записи сочетания, как в «Клавиатура → Сочетания клавиш» в настройках macOS.
private struct ShortcutField: View {
    let cancel: () -> Void
    var body: some View {
        HStack(spacing: 6) {
            Text("Нажмите сочетание")
                .foregroundStyle(.secondary)
                .padding(.horizontal, 10)
                .padding(.vertical, 4)
                .frame(minWidth: 170)
                .background(RoundedRectangle(cornerRadius: 6).fill(Color(nsColor: .textBackgroundColor)))
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.accentColor, lineWidth: 2))
            Button(action: cancel) { Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary) }
                .buttonStyle(.plain)
                .help("Отмена (Esc)")
        }
    }
}

struct HotkeysTab: View {
    @ObservedObject var model: SettingsModel
    @State private var recorder = HotkeyRecorder()
    @State private var recording: SettingsModel.Action?

    var body: some View {
        Form {
            Section {
                row("Сменить раскладку", .convert)
                row("Причесать типографику", .typography)
            } footer: {
                VStack(alignment: .leading, spacing: 8) {
                    if let message = model.hotkeyMessage {
                        Text(message).foregroundStyle(.red).frame(maxWidth: .infinity, alignment: .leading)
                    }
                    BelowBlock(hint: "«Свой вариант» — сочетание с ⌘, ⌥ или ⌃, например ⌃⌥T. Esc — отмена.") {
                        Button("По умолчанию") {
                            stopRecording()
                            model.resetHotkeys()
                        }
                    }
                }
            }
        }
        .formStyle(.grouped)
        .onDisappear { stopRecording() }
    }

    private enum Choice: Hashable { case modifier(ModifierKey), custom, record }

    @ViewBuilder
    private func row(_ title: String, _ action: SettingsModel.Action) -> some View {
        LabeledContent(title) {
            if recording == action {
                ShortcutField(cancel: stopRecording)
            } else {
                picker(action).labelsHidden().fixedSize()
            }
        }
    }

    private func picker(_ action: SettingsModel.Action) -> some View {
        let current = model.hotkey(action)
        let selection = Binding<Choice>(
            get: {
                if case .doubleTap(let key) = current { return .modifier(key) }
                return .custom
            },
            set: { choice in
                switch choice {
                case .modifier(let key): model.setHotkey(.doubleTap(key), for: action)
                case .record: record(action)
                case .custom: break
                }
            })
        return Picker("", selection: selection) {
            ForEach(ModifierKey.allCases, id: \.self) { Text($0.title).tag(Choice.modifier($0)) }
            Divider()
            if case .combo = current { Text(current.title).tag(Choice.custom) }
            Text("Свой вариант…").tag(Choice.record)
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

    private func stopRecording() {
        recorder.stop()
        recording = nil
    }
}

struct LearnedTab: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        Form {
            Section {
                if model.learned.isEmpty {
                    Text(T("Пока пусто. Слово попадает сюда, когда ты дважды откатываешь его."))
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
                Hint("Эти слова свитчер сам не трогает.")
            }
        }
        .formStyle(.grouped)
    }
}

/// Окно настроек: одно на всё приложение. Нативная панель настроек macOS —
/// вкладки-иконки в панели инструментов, заголовок окна = название вкладки.
final class SettingsWindow {
    private var window: NSWindow?
    let model: SettingsModel

    init(model: SettingsModel) { self.model = model }

    func show() {
        model.reload()
        if window == nil { window = makeWindow() }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    private func makeWindow() -> NSWindow {
        let tabs = NSTabViewController()
        tabs.tabStyle = .toolbar
        // заголовок окна всегда «Vallo Switcher» — название вкладки и так видно на панели
        tabs.canPropagateSelectedChildViewControllerTitle = false
        func add<V: View>(_ title: String, _ symbol: String, height: CGFloat, _ view: V) {
            let host = NSHostingController(rootView: view.frame(width: 500, height: height))
            host.sizingOptions = .preferredContentSize
            let item = NSTabViewItem(viewController: host)
            item.label = title
            item.image = NSImage(systemSymbolName: symbol, accessibilityDescription: title)
            tabs.addTabViewItem(item)
        }
        add("Основное", "gearshape", height: 230, GeneralTab(model: model))
        add("Исключения", "nosign", height: 520, ExclusionsTab(model: model))
        add("Типографика", "textformat", height: 600, TypographyTab(model: model))
        add("Сочетания", "keyboard", height: 260, HotkeysTab(model: model))
        add("Слова", "text.book.closed", height: 340, LearnedTab(model: model))

        let w = NSWindow(contentViewController: tabs)
        w.styleMask = [.titled, .closable]
        w.toolbarStyle = .preference
        w.title = "Vallo Switcher"
        w.isReleasedWhenClosed = false
        w.center()
        return w
    }
}
