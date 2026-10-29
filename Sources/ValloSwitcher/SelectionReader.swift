import AppKit
import ApplicationServices

/// Достаёт выделенный текст из активного приложения.
enum SelectionReader {
    static let copyKey: CGKeyCode = 8   // C

    /// Сначала через Accessibility. Если приложение его не отдаёт (Figma, Electron) — через Cmd+C.
    /// Буфер обмена после Cmd+C возвращается как был.
    static func selectedText() -> String? {
        switch accessibilitySelection() {
        case .text(let s): return s.isEmpty ? nil : s
        case .unavailable: return copySelection()
        }
    }

    private enum AXResult { case text(String), unavailable }

    private static func accessibilitySelection() -> AXResult {
        let system = AXUIElementCreateSystemWide()
        var focused: CFTypeRef?
        guard AXUIElementCopyAttributeValue(system, kAXFocusedUIElementAttribute as CFString, &focused) == .success,
              let element = focused else { return .unavailable }
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element as! AXUIElement, kAXSelectedTextAttribute as CFString, &value) == .success,
              let text = value as? String else { return .unavailable }
        return .text(text)
    }

    private static func copySelection() -> String? {
        let pasteboard = NSPasteboard.general
        let saved = Clipboard.save()
        let before = pasteboard.changeCount
        EventPoster.postKey(copyKey, flags: .maskCommand)
        // ждём, пока приложение положит выделение в буфер (до 300 мс)
        var waited = 0
        while pasteboard.changeCount == before && waited < 300 {
            usleep(10_000)
            waited += 10
        }
        guard pasteboard.changeCount != before else { return nil }
        let text = pasteboard.string(forType: .string)
        Clipboard.restore(saved)
        return text?.isEmpty == false ? text : nil
    }
}

/// Сохранение и возврат буфера обмена со всеми типами (текст, картинки, файлы).
enum Clipboard {
    typealias Snapshot = [[NSPasteboard.PasteboardType: Data]]

    static func save() -> Snapshot {
        (NSPasteboard.general.pasteboardItems ?? []).map { item in
            var entry: [NSPasteboard.PasteboardType: Data] = [:]
            for type in item.types { entry[type] = item.data(forType: type) }
            return entry
        }
    }

    static func restore(_ snapshot: Snapshot) {
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        let items = snapshot.map { entry -> NSPasteboardItem in
            let item = NSPasteboardItem()
            for (type, data) in entry { item.setData(data, forType: type) }
            return item
        }
        if !items.isEmpty { pasteboard.writeObjects(items) }
    }
}
