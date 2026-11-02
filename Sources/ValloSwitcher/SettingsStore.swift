import Foundation
import ValloCore

/// Настройки. Хранится только это — набранный текст никуда не пишется.
final class SettingsStore {
    private let defaults = UserDefaults.standard

    init() {
        defaults.register(defaults: [
            "autoSwitchEnabled": true,
            "excludedBundleIDs": ["com.apple.Terminal", "com.googlecode.iterm2", "com.microsoft.VSCode"],
            "learnedWords": [String](),
            "debugMode": false,
            "typographyLiveEnabled": true,
            "typoLiveEnabled": true,
            "yoEnabled": true,
            "typoIgnoredWords": [String](),
            "typoCapitalizeEnabled": false,
            "typographyHotkeyEnabled": true,
            "typographyExcludedBundleIDs": ["com.apple.Terminal", "com.googlecode.iterm2", "com.microsoft.VSCode",
                                            "com.apple.dt.Xcode", "com.todesktop.230313mzl4w4u92"],
        ])
    }

    var autoSwitchEnabled: Bool {
        get { defaults.bool(forKey: "autoSwitchEnabled") }
        set { defaults.set(newValue, forKey: "autoSwitchEnabled") }
    }

    var excludedBundleIDs: [String] {
        get { defaults.stringArray(forKey: "excludedBundleIDs") ?? [] }
        set { defaults.set(newValue, forKey: "excludedBundleIDs") }
    }

    var learnedWords: [String] {
        get { defaults.stringArray(forKey: "learnedWords") ?? [] }
        set { defaults.set(newValue, forKey: "learnedWords") }
    }

    var debugMode: Bool {
        get { defaults.bool(forKey: "debugMode") }
        set { defaults.set(newValue, forKey: "debugMode") }
    }

    var typographyLiveEnabled: Bool {
        get { defaults.bool(forKey: "typographyLiveEnabled") }
        set { defaults.set(newValue, forKey: "typographyLiveEnabled") }
    }

    var typographyHotkeyEnabled: Bool {
        get { defaults.bool(forKey: "typographyHotkeyEnabled") }
        set { defaults.set(newValue, forKey: "typographyHotkeyEnabled") }
    }

    /// Исключения типографики — свои, отдельно от свитчера. Плюс всё от JetBrains (см. ContextGuard).
    var typographyExcludedBundleIDs: [String] {
        get { defaults.stringArray(forKey: "typographyExcludedBundleIDs") ?? [] }
        set { defaults.set(newValue, forKey: "typographyExcludedBundleIDs") }
    }

    var hotkeyConvert: Hotkey {
        get { hotkey("hotkeyConvert") ?? .defaultConvert }
        set { setHotkey(newValue, "hotkeyConvert") }
    }

    var hotkeyTypography: Hotkey {
        get { hotkey("hotkeyTypography") ?? .defaultTypography }
        set { setHotkey(newValue, "hotkeyTypography") }
    }

    private func hotkey(_ key: String) -> Hotkey? {
        defaults.data(forKey: key).flatMap { try? JSONDecoder().decode(Hotkey.self, from: $0) }
    }

    private func setHotkey(_ value: Hotkey, _ key: String) {
        defaults.set(try? JSONEncoder().encode(value), forKey: key)
    }

    var typoLiveEnabled: Bool {
        get { defaults.bool(forKey: "typoLiveEnabled") }
        set { defaults.set(newValue, forKey: "typoLiveEnabled") }
    }

    /// Расставлять «ё» — на лету и по правому Shift, независимо от опечаток и типографики.
    var yoEnabled: Bool {
        get { defaults.bool(forKey: "yoEnabled") }
        set { defaults.set(newValue, forKey: "yoEnabled") }
    }

    /// «Не исправлять» — слова, исправление которых человек отменил или добавил сам.
    var typoIgnoredWords: [String] {
        get { defaults.stringArray(forKey: "typoIgnoredWords") ?? [] }
        set { defaults.set(newValue, forKey: "typoIgnoredWords") }
    }

    var typoCapitalizeEnabled: Bool {
        get { defaults.bool(forKey: "typoCapitalizeEnabled") }
        set { defaults.set(newValue, forKey: "typoCapitalizeEnabled") }
    }

    func ignoreTypo(_ word: String) {
        guard !typoIgnoredWords.contains(word) else { return }
        typoIgnoredWords.append(word)
    }

    func learn(_ word: String) {
        guard !learnedWords.contains(word) else { return }
        learnedWords.append(word)
    }
}
