import AppKit
import ApplicationServices

/// Разрешения «Универсальный доступ» и «Мониторинг ввода».
enum Permissions {
    static var accessibility: Bool { AXIsProcessTrusted() }
    static var inputMonitoring: Bool { CGPreflightListenEventAccess() }
    static var allGranted: Bool { accessibility && inputMonitoring }

    /// Как называется список «Универсальный доступ» в Конфиденциальности на этом Маке:
    /// в macOS 27 Apple переименовала его в «Управление устройством и доступ к данным».
    static var accessibilityName: String {
        ProcessInfo.processInfo.operatingSystemVersion.majorVersion >= 27
            ? "Управление устройством и доступ к данным" : "Универсальный доступ"
    }

    /// То же под другим именем — на случай, если версия угадана неверно.
    static var accessibilityOtherName: String {
        ProcessInfo.processInfo.operatingSystemVersion.majorVersion >= 27
            ? "в старых macOS — «Универсальный доступ»" : "в новых macOS — «Управление устройством и доступ к данным»"
    }

    enum Pane: String {
        case accessibility = "Privacy_Accessibility"
        case inputMonitoring = "Privacy_ListenEvent"
    }

    /// Открывает нужный список в Системных настройках и добавляет туда свитчер (галочку ставит человек).
    static func open(_ pane: Pane) {
        switch pane {
        case .accessibility:
            let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
            _ = AXIsProcessTrustedWithOptions(options)
        case .inputMonitoring:
            _ = CGRequestListenEventAccess()
        }
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?\(pane.rawValue)")!)
    }
}
