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
            "emojiPreset": "millennial",
            "emojiCustom": [[String: String]](),
            "typoIgnoredWords": [String](),
            "typoCapitalizeEnabled": false,
            "typographyHotkeyEnabled": true,
            "figmaPluginSeen": false,
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

    /// Плагин Vallo в Фигме хоть раз был на связи — только тогда свитчер сам его запускает.
    /// Карточку «Где значок «ё»» закрыли кнопкой «Понятно».
    var menuBarHintDismissed: Bool {
        get { defaults.bool(forKey: "menuBarHintDismissed") }
        set { defaults.set(newValue, forKey: "menuBarHintDismissed") }
    }

    var figmaPluginSeen: Bool {
        get { defaults.bool(forKey: "figmaPluginSeen") }
        set { defaults.set(newValue, forKey: "figmaPluginSeen") }
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

    /// Смайлики из скобок: "millennial", "zoomer", "custom" или "off".
    var emojiPreset: String {
        get { defaults.string(forKey: "emojiPreset") ?? "millennial" }
        set { defaults.set(newValue, forKey: "emojiPreset") }
    }

    /// Свой набор: пары {trigger: «))», emoji: «😄»} в порядке, как на странице настроек.
    var emojiCustom: [[String: String]] {
        get { defaults.array(forKey: "emojiCustom") as? [[String: String]] ?? [] }
        set { defaults.set(newValue, forKey: "emojiCustom") }
    }

    /// Какой набор смайликов включён на лету; nil — выключено.
    var emojiBrackets: EmojiBrackets? {
        switch emojiPreset {
        case "off": return nil
        case "zoomer": return .zoomer
        case "custom":
            var pairs: [String: String] = [:]
            for p in emojiCustom { if let t = p["trigger"], let e = p["emoji"] { pairs[t] = e } }
            return EmojiBrackets(pairs: pairs)
        default: return .millennial
        }
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
