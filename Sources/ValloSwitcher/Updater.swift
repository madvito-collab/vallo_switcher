import AppKit
import Sparkle
import UserNotifications

/// Автообновление через Sparkle: список версий — appcast.xml в репозитории, DMG — в GitHub Releases.
/// Раз в день проверяет; «Обновлять автоматически» включено — тихо скачивает и ставит при перезапуске,
/// выключено — спрашивает «Вышла новая версия — обновить?».
final class Updater: NSObject, SPUUpdaterDelegate, SPUStandardUserDriverDelegate, UNUserNotificationCenterDelegate {
    private var controller: SPUStandardUpdaterController!

    /// Есть новая версия: строка «готово к установке» или «вышла, обновить?» — бейдж на значке и пункт в меню.
    enum Pending: Equatable {
        case ready(String)       // скачана, ставится перезапуском
        case available(String)   // автообновление выключено — ждёт согласия
    }
    private(set) var pending: Pending? { didSet { onPendingChange?(pending) } }
    var onPendingChange: ((Pending?) -> Void)?
    /// Поставить скачанное обновление и перезапустить свитчер (даёт Sparkle).
    private var installNowHandler: (() -> Void)?

    override init() {
        super.init()
        controller = SPUStandardUpdaterController(startingUpdater: true, updaterDelegate: self, userDriverDelegate: self)
        UNUserNotificationCenter.current().delegate = self
    }

    /// Пункт меню / уведомление: скачано — перезапуститься с новой версией, иначе — окно Sparkle.
    func installPending() {
        if case .ready = pending, let installNowHandler { installNowHandler() } else { checkNow() }
    }

    // MARK: - Sparkle

    /// Обновление скачано и ждёт выхода из свитчера. Свитчер не закрывают неделями —
    /// поэтому берём установку на себя: бейдж, пункт меню и уведомление «перезапустить».
    func updater(_ updater: SPUUpdater, willInstallUpdateOnQuit item: SUAppcastItem,
                 immediateInstallationBlock immediateInstallHandler: @escaping () -> Void) -> Bool {
        installNowHandler = immediateInstallHandler
        pending = .ready(item.displayVersionString)
        Updater.notifyReady(version: item.displayVersionString)
        return true
    }

    /// Автообновление выключено, плановая проверка нашла версию: Sparkle сам покажет окно, а мы — бейдж,
    /// пока человек не ответит.
    func standardUserDriverWillHandleShowingUpdate(_ handleShowingUpdate: Bool, forUpdate update: SUAppcastItem,
                                                   state: SPUUserUpdateState) {
        if !state.userInitiated { pending = .available(update.displayVersionString) }
    }

    func standardUserDriverDidReceiveUserAttention(forUpdate update: SUAppcastItem) {
        if case .available = pending { pending = nil }
    }

    // MARK: - Уведомления

    static func notifyReady(version: String) {
        let center = UNUserNotificationCenter.current()
        center.requestAuthorization(options: [.alert]) { granted, _ in
            guard granted else { return }
            let content = UNMutableNotificationContent()
            content.title = "Обновление \(version) готово"
            content.body = "Нажмите, чтобы перезапустить свитчер — это пара секунд"
            center.add(UNNotificationRequest(identifier: "update-ready", content: content, trigger: nil))
        }
    }

    /// Клик по уведомлению «готово» — ставим.
    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
                                withCompletionHandler completionHandler: @escaping () -> Void) {
        if response.notification.request.identifier == "update-ready" {
            DispatchQueue.main.async { self.installPending() }
        }
        completionHandler()
    }

    /// Свитчер — фоновое приложение, но уведомления показываем и когда открыто его окно.
    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner])
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
