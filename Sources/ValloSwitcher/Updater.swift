import AppKit
import Sparkle
import UserNotifications

/// Автообновление через Sparkle: список версий — appcast.xml в репозитории, DMG — в GitHub Releases.
/// Раз в день проверяет; «Обновлять автоматически» включено — тихо скачивает и ставит при перезапуске,
/// выключено — спрашивает «Вышла новая версия — обновить?».
final class Updater: NSObject, SPUStandardUserDriverDelegate {
    private var controller: SPUStandardUpdaterController!

    override init() {
        super.init()
        controller = SPUStandardUpdaterController(startingUpdater: true, updaterDelegate: nil, userDriverDelegate: self)
    }

    var automaticallyUpdates: Bool {
        get { controller.updater.automaticallyDownloadsUpdates }
        set { controller.updater.automaticallyDownloadsUpdates = newValue }
    }

    var lastCheck: Date? { controller.updater.lastUpdateCheckDate }

    /// «Проверить обновления…» в меню.
    func checkNow() {
        NSApp.activate(ignoringOtherApps: true)
        controller.checkForUpdates(nil)
    }

    /// Свитчер живёт в строке меню без окна: Sparkle напоминает о новой версии мягко, не вылезая поверх всего.
    var supportsGentleScheduledUpdateReminders: Bool { true }

    /// После обновления — уведомление «Обновлено до 0.6». Версию прошлого запуска помним в настройках.
    static func announceIfUpdated() {
        let current = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? ""
        let key = "lastRunVersion"
        let previous = UserDefaults.standard.string(forKey: key)
        UserDefaults.standard.set(current, forKey: key)
        // первый запуск (или с версии без этой памяти) — молчим: пользователь ставил сам и так знает
        guard let previous, previous != current else { return }
        let center = UNUserNotificationCenter.current()
        center.requestAuthorization(options: [.alert]) { granted, _ in
            guard granted else { return }
            let content = UNMutableNotificationContent()
            content.title = "Vallo Switcher"
            content.body = "Обновлено до \(current)"
            center.add(UNNotificationRequest(identifier: "updated", content: content, trigger: nil))
        }
    }
}
