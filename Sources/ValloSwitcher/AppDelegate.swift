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
    private var typos: TypoCorrector!
    private let modelStore = ModelStore()

    func applicationDidFinishLaunching(_ notification: Notification) {
        // свитчер уже запущен (например, второй раз из «Программ» или из DMG) — второй экземпляр не нужен:
        // два перехвата клавиатуры исправляли бы каждое слово дважды
        let others = NSRunningApplication.runningApplications(withBundleIdentifier: Bundle.main.bundleIdentifier ?? "")
            .filter { $0 != .current }
        if !others.isEmpty {
            others.first?.activate()
            NSApp.terminate(nil)
            return
        }
        menu = StatusMenu(store: store, modelStore: modelStore)
        DebugLog.enabled = store.debugMode
        do {
            layouts = try SystemLayouts()
            engine = Engine(layouts: layouts.pair, judge: try loadJudge(), currentLanguage: layouts.current ?? .en)
            typos = try TypoCorrector.load(url: Bundle.main.resourceURL!.appendingPathComponent("wordlists/ru-typos.txt"),
                                           ignored: Set(store.typoIgnoredWords))
        } catch {
            fail("Не получилось запуститься: \(error)")
            return
        }
        let model = SettingsModel(store: store)
        model.onLearnedChanged = { [weak self] words in self?.engine.judge.learned = Set(words) }
        model.onTypoWordsChanged = { [weak self] words in
            self?.typos.ignored = Set(words.map(Lexicon.normalize))
            self?.tap?.typos.ignored = Set(words.map(Lexicon.normalize))
        }
        model.onHotkeysChanged = { [weak self] in self?.tap?.reloadHotkeys() }
        settings = SettingsWindow(model: model, modelStore: modelStore)
        menu.onOpenSettings = { [weak self] in self?.settings.show() }
        menu.onDownloadModel = { [weak self] in
            guard let self else { return }
            self.settings.show(tab: .typos)
            if case .downloading = self.modelStore.state { return }
            self.modelStore.download()
        }

        layouts.onExternalChange = { [weak self] lang in
            // третья раскладка: просто сбрасываем память, движок молчит, пока не вернутся RU/EN
            guard let lang else { self?.engine.reset(); return }
            self?.engine.externalLayoutChange(to: lang)
        }
        layouts.onLayoutsChanged = { [weak self] pair in self?.engine.updateLayouts(pair) }

        // Мак сообщает о любой смене разрешений «Универсального доступа» — сверяемся сразу
        DistributedNotificationCenter.default().addObserver(
            forName: NSNotification.Name("com.apple.accessibility.api"), object: nil, queue: .main
        ) { [weak self] _ in
            // разрешение обновляется чуть позже уведомления
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) { self?.checkPermissions() }
        }

        menu.onGivePermissions = { [weak self] in self?.showOnboarding() }
        onboarding.model.onGranted = { [weak self] in self?.startTap() }
        if Permissions.allGranted {
            startTap()
            // предпросмотр онбординга: open -a "Vallo Switcher" --args --onboarding
            if CommandLine.arguments.contains("--onboarding") { showOnboarding(preview: true) }
        } else {
            menu.set(state: .needsPermissions)
            showOnboarding()
        }
    }

    private func showOnboarding(preview: Bool = false) {
        onboarding.show(preview: preview, convertTitle: store.hotkeyConvert.title, typographyTitle: store.hotkeyTypography.title)
    }

    private func startTap() {
        guard tap == nil else { return }
        let tap = KeyboardTap(engine: engine, store: store, layouts: layouts, typos: typos)
        modelStore.onChange = { [weak tap] in tap?.commas.unload() }
        tap.commas.onLoadFailed = { [weak self] in self?.modelStore.markBroken() }
        tap.onTypoUndone = { [weak self] word in
            self?.store.ignoreTypo(word)
            self?.settings.model.reloadTypoWords()
        }
        tap.onDecision = { [weak self] in self?.menu.record(decision: $0) }
        tap.onPermissionLost = { [weak self] in self?.checkPermissions() }
        guard tap.start() else {
            menu.set(state: .needsPermissions)
            return
        }
        self.tap = tap
        menu.set(state: .working)
        // раз в секунду: не отобрали ли разрешения; пока активно поле пароля — серая иконка
        secureInputTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            guard let self, self.checkPermissions() else { return }
            self.menu.set(state: ContextGuard.secureInputActive ? .blocked : .working)
        }
    }

    /// Разрешения на месте? Если отобрали — немедленно снимаем перехват, иначе встанет вся система.
    @discardableResult
    private func checkPermissions() -> Bool {
        guard tap != nil else { return Permissions.allGranted }
        guard !Permissions.allGranted else { return true }
        tap?.stop()
        tap = nil
        secureInputTimer?.invalidate()
        secureInputTimer = nil
        menu.set(state: .needsPermissions)
        showOnboarding()
        return false
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
