import AppKit
import ValloCore

final class AppDelegate: NSObject, NSApplicationDelegate {
    private let store = SettingsStore()
    private var menu: StatusMenu!
    private var layouts: SystemLayouts!
    private var engine: Engine!
    private var tap: KeyboardTap?
    private let onboarding = OnboardingWindow()
    private var secureInputTimer: Timer?
    private var settings: SettingsWindow!

    func applicationDidFinishLaunching(_ notification: Notification) {
        menu = StatusMenu(store: store)
        DebugLog.enabled = store.debugMode
        do {
            layouts = try SystemLayouts()
            engine = Engine(layouts: layouts.pair, judge: try loadJudge(), currentLanguage: layouts.current ?? .en)
        } catch {
            fail("Не получилось запуститься: \(error)")
            return
        }
        let model = SettingsModel(store: store)
        model.onLearnedChanged = { [weak self] words in self?.engine.judge.learned = Set(words) }
        settings = SettingsWindow(model: model)
        menu.onOpenSettings = { [weak self] in self?.settings.show() }

        layouts.onExternalChange = { [weak self] lang in
            // третья раскладка: просто сбрасываем память, движок молчит, пока не вернутся RU/EN
            guard let lang else { self?.engine.reset(); return }
            self?.engine.externalLayoutChange(to: lang)
        }
        layouts.onLayoutsChanged = { [weak self] pair in self?.engine.updateLayouts(pair) }

        menu.onGivePermissions = { [weak self] in self?.onboarding.show() }
        onboarding.model.onGranted = { [weak self] in self?.startTap() }
        if Permissions.allGranted {
            startTap()
        } else {
            menu.set(state: .needsPermissions)
            onboarding.show()
        }
    }

    private func startTap() {
        guard tap == nil else { return }
        let tap = KeyboardTap(engine: engine, store: store, layouts: layouts)
        tap.onDecision = { [weak self] in self?.menu.record(decision: $0) }
        guard tap.start() else {
            menu.set(state: .needsPermissions)
            return
        }
        self.tap = tap
        menu.set(state: .working)
        // пока активно поле пароля, Мак закрывает клавиатуру — показываем серую иконку
        secureInputTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            self?.menu.set(state: ContextGuard.secureInputActive ? .blocked : .working)
        }
    }

    private func loadJudge() throws -> Judge {
        let dir = Bundle.main.resourceURL!.appendingPathComponent("wordlists")
        return Judge(en: try Lexicon.load(url: dir.appendingPathComponent("en.txt"), language: .en),
                     ru: try Lexicon.load(url: dir.appendingPathComponent("ru.txt"), language: .ru),
                     learned: Set(store.learnedWords))
    }

    private func fail(_ message: String) {
        let alert = NSAlert()
        alert.messageText = "Vallo Switcher"
        alert.informativeText = message
        alert.runModal()
        NSApp.terminate(nil)
    }
}
