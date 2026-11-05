import AppKit
import UserNotifications

/// Запуск плагина Vallo в Фигме без рук: быстрый поиск (⌘/) → «Vallo» → Enter.
/// Фигма сама запускать плагины не умеет — их запускают в каждом открытом файле.
enum FigmaLauncher {
    static let figmaBundleID = "com.figma.Desktop"

    static func launch() {
        EventPoster.postKey(44, flags: .maskCommand)            // ⌘/
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
            EventPoster.insert("Vallo")
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                EventPoster.postKey(36)                          // Return
            }
        }
    }

    /// Не получилось запустить — пусть человек запустит сам.
    static func showHint() {
        let center = UNUserNotificationCenter.current()
        center.requestAuthorization(options: [.alert]) { granted, _ in
            guard granted else { return }
            let content = UNMutableNotificationContent()
            content.title = "Vallo Switcher"
            content.body = "Запустите плагин Vallo Typograph в Фигме вручную: «Плагины → Vallo Typograph»"
            center.add(UNNotificationRequest(identifier: "figma-plugin", content: content, trigger: nil))
        }
    }
}
