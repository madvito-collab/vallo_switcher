import Foundation

/// Настройки. Хранится только это — набранный текст никуда не пишется.
final class SettingsStore {
    private let defaults = UserDefaults.standard

    init() {
        defaults.register(defaults: [
            "autoSwitchEnabled": true,
            "excludedBundleIDs": ["com.apple.Terminal", "com.googlecode.iterm2", "com.microsoft.VSCode"],
            "learnedWords": [String](),
            "debugMode": false,
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

    func learn(_ word: String) {
        guard !learnedWords.contains(word) else { return }
        learnedWords.append(word)
    }
}
