import AppKit
import ApplicationServices

/// Разрешения «Универсальный доступ» и «Мониторинг ввода».
enum Permissions {
    static var accessibility: Bool { AXIsProcessTrusted() }
    static var inputMonitoring: Bool { CGPreflightListenEventAccess() }
    static var allGranted: Bool { accessibility && inputMonitoring }

    /// Добавляет приложение в оба списка в Системных настройках (галочку ставит человек).
    static func request() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
        _ = CGRequestListenEventAccess()
    }

    static func openSettings() {
        let pane = accessibility ? "Privacy_ListenEvent" : "Privacy_Accessibility"
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?\(pane)")!)
    }

    /// Окно первого запуска: что дать и зачем.
    static func explain() {
        let alert = NSAlert()
        alert.messageText = "Vallo Switcher нужны два разрешения"
        alert.informativeText = """
        «Универсальный доступ» — чтобы исправлять текст: стирать и печатать заново.
        «Мониторинг ввода» — чтобы видеть, что ты печатаешь, и ловить двойной Shift.

        Набранный текст никуда не сохраняется и не отправляется.
        Открой Системные настройки → Конфиденциальность и безопасность и включи Vallo Switcher в обоих списках.
        """
        alert.addButton(withTitle: "Открыть настройки")
        alert.addButton(withTitle: "Позже")
        NSApp.activate(ignoringOtherApps: true)
        if alert.runModal() == .alertFirstButtonReturn {
            request()
            openSettings()
        }
    }
}
