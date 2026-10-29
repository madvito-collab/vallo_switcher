import AppKit
import ValloCore

/// «Руки»: стирают, печатают, переключают раскладку.
extension Array where Element == Action {
    /// Для журнала: виды действий без самого текста.
    var summary: String {
        map { action -> String in
            switch action {
            case .delete(let n): return "delete(\(n))"
            case .insert(let s): return "insert(\(s.count) chars)"
            case .switchLayout(let l): return "switch(\(l.rawValue))"
            case .replaceSelection(let s): return "replaceSelection(\(s.count) chars)"
            case .learn: return "learn"
            }
        }.joined(separator: ", ")
    }
}

enum EventPoster {
    /// Метка своих событий («VALL»), чтобы тап их пропускал.
    static let marker: Int64 = 0x56414C4C
    static let backspaceKey: CGKeyCode = 51

    /// `proxy` — когда вызываем из колбэка тапа: события встают сразу за текущей клавишей.
    static func perform(_ actions: [Action], store: SettingsStore, layouts: SystemLayouts, proxy: CGEventTapProxy? = nil) {
        for action in actions {
            switch action {
            case .delete(let n):
                for _ in 0..<n { postKey(backspaceKey, proxy: proxy) }
            case .insert(let text):
                insert(text, proxy: proxy)
            case .switchLayout(let language):
                layouts.select(language)
            case .learn(let word):
                store.learn(word)
            case .replaceSelection(let text):
                replaceSelection(text)
            }
        }
    }

    static func postKey(_ key: CGKeyCode, flags: CGEventFlags = [], proxy: CGEventTapProxy? = nil) {
        for down in [true, false] {
            guard let e = CGEvent(keyboardEventSource: nil, virtualKey: key, keyDown: down) else { continue }
            e.flags = flags
            post(e, proxy: proxy)
        }
    }

    private static func post(_ e: CGEvent, proxy: CGEventTapProxy?) {
        e.setIntegerValueField(.eventSourceUserData, value: marker)
        if let proxy { e.tapPostEvent(proxy) } else { e.post(tap: .cghidEventTap) }
    }

    static let pasteKey: CGKeyCode = 9   // V

    /// Вставляет текст вместо выделения через буфер обмена, потом возвращает буфер как был.
    static func replaceSelection(_ text: String) {
        let saved = Clipboard.save()
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.setString(text, forType: .string)
        // временная запись: менеджеры буфера (Paste, Maccy) её не сохраняют
        pasteboard.setData(Data(), forType: NSPasteboard.PasteboardType("org.nspasteboard.TransientType"))
        let ours = pasteboard.changeCount
        postKey(pasteKey, flags: .maskCommand)
        // Electron и Figma читают буфер не сразу; и не затираем то, что человек успел скопировать сам
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.7) {
            if pasteboard.changeCount == ours { Clipboard.restore(saved) }
        }
    }

    /// Печатает текст напрямую символами — не зависит от текущей раскладки.
    static func insert(_ text: String, proxy: CGEventTapProxy? = nil) {
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
            post(down, proxy: proxy)
            post(up, proxy: proxy)
        }
    }
}
