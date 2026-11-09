import AppKit
import ServiceManagement
import ValloCore

/// Модель окна настроек поверх SettingsStore.
final class SettingsModel: ObservableObject {
    private let store: SettingsStore
    private let updater: Updater
    /// Список запомненных слов поменялся — движку надо обновить судью.
    var onLearnedChanged: (([String]) -> Void)?
    /// «Не исправлять» поменялись — исправлятору надо обновить.
    var onTypoWordsChanged: (([String]) -> Void)?
    /// Сочетания поменялись — перехвату надо перечитать.
    var onHotkeysChanged: (() -> Void)?

    @Published var autoSwitch: Bool { didSet { store.autoSwitchEnabled = autoSwitch } }
    /// Хранит сам Sparkle — у него своя настройка.
    @Published var autoUpdate: Bool { didSet { updater.automaticallyUpdates = autoUpdate } }
    func checkForUpdates() { updater.checkNow() }
    var version: String { Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "" }
    @Published var debugMode: Bool {
        didSet {
            store.debugMode = debugMode
            DebugLog.enabled = debugMode
        }
    }
    @Published var excluded: [String] { didSet { store.excludedBundleIDs = excluded } }
    @Published var typographyLive: Bool { didSet { store.typographyLiveEnabled = typographyLive } }
    @Published var typoLive: Bool { didSet { store.typoLiveEnabled = typoLive } }
    @Published var yo: Bool { didSet { store.yoEnabled = yo } }
    @Published var emojiPreset: String { didSet { store.emojiPreset = emojiPreset } }
    @Published var menuBarHintDismissed: Bool { didSet { store.menuBarHintDismissed = menuBarHintDismissed } }
    @Published var emojiCustom: [[String: String]] { didSet { store.emojiCustom = emojiCustom } }
    @Published var typoCapitalize: Bool { didSet { store.typoCapitalizeEnabled = typoCapitalize } }
    @Published var typoWords: [String] {
        didSet {
            store.typoIgnoredWords = typoWords
            onTypoWordsChanged?(typoWords)
        }
    }
    @Published var typographyHotkey: Bool { didSet { store.typographyHotkeyEnabled = typographyHotkey } }
    @Published var typographyExcluded: [String] { didSet { store.typographyExcludedBundleIDs = typographyExcluded } }
    @Published private(set) var hotkeyConvert: Hotkey
    @Published private(set) var hotkeyTypography: Hotkey
    /// Сообщение под сочетаниями: конфликт или «нажми сочетание».
    @Published var hotkeyMessage: String?
    /// К какому сочетанию относится сообщение — его показываем под этой строкой.
    @Published var hotkeyMessageAction: Action?
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

    init(store: SettingsStore, updater: Updater) {
        self.store = store
        self.updater = updater
        autoSwitch = store.autoSwitchEnabled
        autoUpdate = updater.automaticallyUpdates
        debugMode = store.debugMode
        excluded = store.excludedBundleIDs
        typographyLive = store.typographyLiveEnabled
        typoLive = store.typoLiveEnabled
        yo = store.yoEnabled
        emojiPreset = store.emojiPreset
        menuBarHintDismissed = store.menuBarHintDismissed
        emojiCustom = store.emojiCustom
        typoCapitalize = store.typoCapitalizeEnabled
        typoWords = store.typoIgnoredWords
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
    /// Слово попало в «Не исправлять», пока окно открыто.
    func reloadTypoWords() {
        if typoWords != store.typoIgnoredWords { typoWords = store.typoIgnoredWords }
    }

    /// Добавить слово в «Не исправлять» вручную.
    func addTypoWord(_ raw: String) {
        let w = Lexicon.normalize(raw.trimmingCharacters(in: .whitespaces))
        guard !w.isEmpty, !typoWords.contains(w) else { return }
        typoWords.append(w)
    }

    /// Добавить слово в «Не переключать» вручную.
    func addLearnedWord(_ raw: String) {
        let w = Lexicon.normalize(raw.trimmingCharacters(in: .whitespaces))
        guard !w.isEmpty, !learned.contains(w) else { return }
        learned.append(w)
    }

    /// Когда свитчер последний раз проверял обновления.
    var lastUpdateCheck: Date? { updater.lastCheck }

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
            hotkeyMessage = action == .convert ? "Это сочетание уже исправляет текст" : "Это сочетание уже меняет раскладку"
            hotkeyMessageAction = action
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

    /// Только установленные приложения — для показа. Исключения по умолчанию для приложений,
    /// которых нет (Cursor, Xcode…), остаются в списке и сработают, если приложение поставят.
    func installed(_ ids: [String]) -> [String] {
        ids.filter { NSWorkspace.shared.urlForApplication(withBundleIdentifier: $0) != nil }
    }

    /// Имя приложения по bundle ID — для списка.
    func appName(_ id: String) -> String {
        let known = ["com.googlecode.iterm2": "iTerm", "com.todesktop.230313mzl4w4u92": "Cursor",
                     "com.apple.Terminal": "Терминал", "com.microsoft.VSCode": "Visual Studio Code", "com.apple.dt.Xcode": "Xcode"]
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: id) else { return known[id] ?? id }
        return FileManager.default.displayName(atPath: url.path).replacingOccurrences(of: ".app", with: "")
    }
}

/// Автозамена macOS включена? Нет ключа в общих настройках — значит, включена (так по умолчанию).
func macAutocorrectEnabled() -> Bool {
    let value = CFPreferencesCopyAppValue("NSAutomaticSpellingCorrectionEnabled" as CFString, kCFPreferencesAnyApplication)
    return (value as? Bool) ?? true
}
