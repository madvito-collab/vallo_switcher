import CoreGraphics
import ValloCore

/// «Руки»: стирают, печатают, переключают раскладку.
enum EventPoster {
    /// Метка своих событий («VALL»), чтобы тап их пропускал.
    static let marker: Int64 = 0x56414C4C
    static let backspaceKey: CGKeyCode = 51

    static func perform(_ actions: [Action], store: SettingsStore, layouts: SystemLayouts) {
        for action in actions {
            switch action {
            case .delete(let n):
                for _ in 0..<n { postKey(backspaceKey) }
            case .insert(let text):
                insert(text)
            case .switchLayout(let language):
                layouts.select(language)
            case .learn(let word):
                store.learn(word)
            case .replaceSelection:
                break // Task 8
            }
        }
    }

    static func postKey(_ key: CGKeyCode, flags: CGEventFlags = []) {
        for down in [true, false] {
            guard let e = CGEvent(keyboardEventSource: nil, virtualKey: key, keyDown: down) else { continue }
            e.flags = flags
            e.setIntegerValueField(.eventSourceUserData, value: marker)
            e.post(tap: .cghidEventTap)
        }
    }

    /// Печатает текст напрямую символами — не зависит от текущей раскладки.
    static func insert(_ text: String) {
        let units = Array(text.utf16)
        var i = 0
        while i < units.count {
            let chunk = Array(units[i..<min(i + 20, units.count)])
            i += 20
            guard let down = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: true),
                  let up = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: false) else { continue }
            down.flags = []
            up.flags = []
            down.keyboardSetUnicodeString(stringLength: chunk.count, unicodeString: chunk)
            down.setIntegerValueField(.eventSourceUserData, value: marker)
            up.setIntegerValueField(.eventSourceUserData, value: marker)
            down.post(tap: .cghidEventTap)
            up.post(tap: .cghidEventTap)
        }
    }
}
