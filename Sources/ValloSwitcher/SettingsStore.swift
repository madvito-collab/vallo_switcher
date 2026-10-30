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

    func learn(_ word: String) {
        guard !learnedWords.contains(word) else { return }
        learnedWords.append(word)
    }
}
