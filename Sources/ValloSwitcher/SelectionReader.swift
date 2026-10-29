import AppKit
import ApplicationServices

/// Достаёт выделенный текст из активного приложения.
enum SelectionReader {
    static let copyKey: CGKeyCode = 8   // C

    /// Сначала через Accessibility. Если приложение его не отдаёт (Figma, Electron) — через Cmd+C.
    /// Буфер обмена после Cmd+C возвращается как был.
    static func selectedText() -> String? {
        enableAccessibilityInFrontApp()
        var result = accessibilitySelection()
        if case .text("") = result {
            // Chrome и Electron строят дерево Accessibility не сразу после включения
            usleep(50_000)
            result = accessibilitySelection()
        }
        log.debug("AX selection: \(String(describing: result), privacy: .public)")
        switch result {
        case .text(let s) where !s.isEmpty: return s
        // пустой ответ Chrome/Electron не означает «нет выделения» — спрашиваем через Cmd+C
        case .text: return isChromium() ? copySelection() : nil
        case .unavailable: return copySelection()
        }
    }

    /// Chrome и Electron-приложения отдают текст через Accessibility, только если их попросить.
    private static func enableAccessibilityInFrontApp() {
        guard let pid = NSWorkspace.shared.frontmostApplication?.processIdentifier else { return }
        let app = AXUIElementCreateApplication(pid)
        AXUIElementSetAttributeValue(app, "AXManualAccessibility" as CFString, kCFBooleanTrue)
    }

    /// Браузеры и приложения на движке Chromium (у них внутри Electron/Chromium Framework).
    private static func isChromium() -> Bool {
        guard let url = NSWorkspace.shared.frontmostApplication?.bundleURL else { return false }
        let frameworks = url.appendingPathComponent("Contents/Frameworks")
        let names = (try? FileManager.default.contentsOfDirectory(atPath: frameworks.path)) ?? []
        return names.contains { $0.contains("Electron Framework") || $0.contains("Chromium") || $0.contains("Chrome Framework") }
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
