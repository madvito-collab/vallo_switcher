import AppKit
import CoreGraphics
import ValloCore

/// «Уши»: перехват клавиатуры и мыши во всех приложениях.
final class KeyboardTap {
    /// Что делать по сочетанию.
    enum Trigger { case convert, typography }

    /// Пока в настройках записывают своё сочетание, сочетания не срабатывают и не глотаются.
    static var recordingHotkey = false

    let engine: Engine
    let store: SettingsStore
    let layouts: SystemLayouts
    /// Перехват снят сам, потому что отобрали доступ.
    var onPermissionLost: (() -> Void)?
    /// Вызывается после каждого решения движка — для меню отладки.
    var onDecision: ((String) -> Void)?

    /// Двойные нажатия модификаторов из настроек.
    private var detectors: [(trigger: Trigger, detector: DoubleTapDetector)] = []
    /// Свои сочетания из настроек.
    private var combos: [(trigger: Trigger, hotkey: Hotkey)] = []
    /// Какие модификаторы (по keyCode) сейчас зажаты.
    private var modifiersDown: Set<Int64> = []
    private var live = LiveTypographer()
    /// Растёт на каждое нажатие и клик — чтобы понять, что человек что-то делал, пока мы ждали модель.
    private var inputGeneration = 0
    /// Исправлятор опечаток; «Мои слова» обновляются снаружи.
    var typos: TypoCorrector
    let yo: Yoficator
    /// Запятые по сочетанию (модель).
    let commas = CommaService()
    /// Связь с плагином Фигмы.
    var figma: FigmaBridge? { didSet { listenToFigma() } }
    /// Человек отменил исправление опечатки — слово в «Не исправлять».
    var onTypoUndone: ((String) -> Void)?
    private var tap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?

    init(engine: Engine, store: SettingsStore, layouts: SystemLayouts, typos: TypoCorrector, yo: Yoficator) {
        self.typos = typos
        self.yo = yo
        self.engine = engine
        self.store = store
        self.layouts = layouts
        reloadHotkeys()
    }

    /// Перечитать сочетания из настроек (после изменения в окне настроек).
    func reloadHotkeys() {
        detectors = []
        combos = []
        for (trigger, hotkey) in [(Trigger.convert, store.hotkeyConvert), (.typography, store.hotkeyTypography)] {
            switch hotkey {
            case .doubleTap(let key): detectors.append((trigger, DoubleTapDetector(key: key)))
            case .combo: combos.append((trigger, hotkey))
            }
        }
    }

    private func otherInput() {
        for i in detectors.indices { detectors[i].detector.otherInput() }
    }

    func start() -> Bool {
        let mask: CGEventMask = [CGEventType.keyDown, .flagsChanged, .leftMouseDown, .rightMouseDown, .otherMouseDown]
            .reduce(0) { $0 | (1 << $1.rawValue) }
        let callback: CGEventTapCallBack = { proxy, type, event, info in
            let me = Unmanaged<KeyboardTap>.fromOpaque(info!).takeUnretainedValue()
            return me.handle(proxy: proxy, type: type, event: event)
        }
        guard let tap = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap,
                                          eventsOfInterest: mask, callback: callback,
                                          userInfo: Unmanaged.passUnretained(self).toOpaque()) else { return false }
        self.tap = tap
        SelectionReader.enableAccessibilityInFrontApp()
        let source = CFMachPortCreateRunLoopSource(nil, tap, 0)
        runLoopSource = source
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)

        NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main
        ) { [weak self] _ in
            log.debug("app activated → reset")
            self?.engine.reset()
            self?.live.reset()
            // Chrome и Electron отдают поля через Accessibility, только если попросить, — просим сразу,
            // иначе охранник не видит, что курсор в поле e-mail или телефона
            SelectionReader.enableAccessibilityInFrontApp()
        }
        return true
    }

    /// Снять перехват. Обязательно, если отобрали «Универсальный доступ»: активный перехват
    /// без разрешения замораживает клавиатуру и мышь во всей системе.
    func stop() {
        if let tap {
            CGEvent.tapEnable(tap: tap, enable: false)
            CFMachPortInvalidate(tap)
        }
        if let runLoopSource { CFRunLoopRemoveSource(CFRunLoopGetMain(), runLoopSource, .commonModes) }
        tap = nil
        runLoopSource = nil
        engine.reset()
        log.debug("tap stopped")
    }

    private func handle(proxy: CGEventTapProxy, type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let tap { CGEvent.tapEnable(tap: tap, enable: true) }
            // пока тап был выключен, мы не видели клавиш — память набора врёт
            engine.reset()
            live.reset()
            otherInput()
            return Unmanaged.passUnretained(event)
        }
        // доступ отобрали на ходу — снимаем перехват немедленно, иначе встанет клавиатура всей системы
        guard AXIsProcessTrusted() else {
            stop()
            DispatchQueue.main.async { [self] in onPermissionLost?() }
            return Unmanaged.passUnretained(event)
        }
        if event.getIntegerValueField(.eventSourceUserData) == EventPoster.marker {
            return Unmanaged.passUnretained(event)
        }
        if type == .keyDown || type == .leftMouseDown || type == .rightMouseDown || type == .otherMouseDown {
            inputGeneration &+= 1
        }
        switch type {
        case .keyDown:
            return handleKeyDown(event, proxy: proxy)
        case .flagsChanged:
            handleFlags(event)
        default:
            log.debug("mouse → reset")
            otherInput()
            engine.reset()
            live.reset()
        }
        return Unmanaged.passUnretained(event)
    }

    private func handleKeyDown(_ event: CGEvent, proxy: CGEventTapProxy) -> Unmanaged<CGEvent>? {
        otherInput()
        let flags = event.flags
        let keyCode = UInt16(event.getIntegerValueField(.keyboardEventKeycode))
        if let trigger = combo(keyCode: keyCode, flags: flags), canFire(trigger) {
            DispatchQueue.main.async { [self] in fire(trigger) }
            return nil
        }
        let stroke = KeyStroke(keyCode: keyCode, shift: flags.contains(.maskShift), capsLock: flags.contains(.maskAlphaShift))
        let modified = !flags.intersection([.maskCommand, .maskControl, .maskAlternate]).isEmpty
        if store.debugMode { ContextGuard.dumpFocusedElement() }
        let allowed = ContextGuard.allowed(store: store)
        engine.autoSwitchEnabled = store.autoSwitchEnabled && allowed.autoSwitch
        // типографика и опечатки на лету — один конвейер; работает, если включено хоть что-то
        let options = LiveOptions(typography: store.typographyLiveEnabled, typos: store.typoLiveEnabled ? typos : nil,
                                  yo: store.yoEnabled ? yo : nil, emoji: store.emojiBrackets)
        let liveAllowed = (options.typography || options.typos != nil || options.yo != nil || options.emoji != nil)
            && allowed.typography
        // ⌥⌫, ⌘⌫ и прочие сочетания меняют текст непредсказуемо — память типографики больше не верна
        if modified || !liveAllowed { live.reset() }

        // поле цвета Фигмы: «аааааа» в русской раскладке → «ffffff» на Enter или Tab (решётку там не пишут)
        if !modified, engine.autoSwitchEnabled, Self.returnKeys.contains(keyCode) || keyCode == Self.tabKey,
           NSWorkspace.shared.frontmostApplication?.bundleIdentifier == FigmaLauncher.figmaBundleID,
           let value = ContextGuard.singleLineFieldValue(), let fixed = HexColor.fix(value, requireHash: false) {
            engine.reset()
            live.reset()
            EventPoster.perform([.delete(value.count), .insert(fixed)], store: store, layouts: layouts, proxy: proxy)
            EventPoster.postKey(CGKeyCode(keyCode), flags: flags.intersection(.maskShift), proxy: proxy)
            return nil
        }

        // Backspace сразу после типографской правки — вернуть набранное
        if keyCode == Engine.backspaceKey && !modified {
            if liveAllowed, let edit = live.backspace() {
                if let word = live.undoneTypoWord {
                    typos.ignored.insert(word)
                    DispatchQueue.main.async { [self] in onTypoUndone?(word) }
                }
                engine.reset()
                EventPoster.perform([.delete(edit.delete), .insert(edit.insert)], store: store, layouts: layouts, proxy: proxy)
                return nil
            }
        }

        // уведомления о смене раскладки иногда не приходят — сверяемся с Маком перед каждой клавишей
        guard let actual = layouts.current else {
            // третья раскладка (украинская, японская…) — свитчер молчит
            engine.reset()
            live.reset()
            return Unmanaged.passUnretained(event)
        }
        if actual != engine.currentLanguage {
            log.debug("layout drift: engine=\(self.engine.currentLanguage.rawValue) actual=\(actual.rawValue)")
            engine.externalLayoutChange(to: actual)
        }
        let output = engine.handleKey(KeyEvent(stroke: stroke, hasCommandControlOrOption: modified))
        log.debug("key → \(output == .passThrough ? "pass" : "consume")")
        if case .consume(let actions) = output {
            // сначала свитчер; на этом нажатии типографика молчит. Свитчер мог исправить слово посреди —
            // дальше в памяти будет хвост слова
            // буква хотя бы в одной раскладке: «,» в EN — это «б» в RU
            let isLetter = { (l: Language) in self.layouts.pair.layout(l).character(for: stroke)?.first?.isLetter ?? false }
            let typedLetter = isLetter(.en) || isLetter(.ru)
            var all = actions
            // слово исправлено на конце (пробел, запятая…) — типографика видит его уже исправленным: «850he,» → «850 ₽»
            if !typedLetter, !modified, liveAllowed,
               case .delete(let n)? = actions.first, actions.count > 1, case .insert(let text) = actions[1],
               let boundary = text.last, !boundary.isLetter,   // Enter и Tab жмутся отдельно — там как раньше
               let edit = live.replaced(delete: n, insert: text, hint: engine.currentLanguage, options: options) {
                all.append(contentsOf: [.delete(edit.delete), .insert(edit.insert)])
                if live.lastEditChangedLength { engine.reset() }
            } else {
                live.reset(midWord: typedLetter)
            }
            report()
            // через proxy: наши события встают сразу за текущей клавишей, раньше следующих нажатий
            EventPoster.perform(all, store: store, layouts: layouts, proxy: proxy)
            return nil
        }

        // типографика на лету
        guard keyCode != Engine.backspaceKey else { return Unmanaged.passUnretained(event) }
        // стрелки, Home/End, Delete раскладка переводит в служебные символы — это не набор, а переход по тексту
        guard !modified, liveAllowed,
              // Enter раскладка не отдаёт (служебная клавиша) — для правки на лету это конец строки
              let text = Self.returnKeys.contains(keyCode) ? "\r" : layouts.pair.layout(actual).character(for: stroke),
              text.count == 1, let char = text.first,
              !LiveTypographer.isNavigation(char) else {
            live.reset()
            return Unmanaged.passUnretained(event)
        }
        // кавычка сразу после клика: что слева, свитчер не видел — спрашиваем у Мака
        if char == "\"" || char == "'", live.needsLeftChar, let left = ContextGuard.charBeforeCaret() {
            live.seedLeft(left)
        }
        guard let edit = live.typed(char, hint: actual, options: options) else { return Unmanaged.passUnretained(event) }
        log.debug("typography → delete(\(edit.delete)) insert(\(edit.insert.count) chars)")
        // длина текста изменилась (… вместо ...) — память свитчера больше не совпадает с экраном
        if live.lastEditChangedLength { engine.reset() }
        // Enter: правку печатаем без него, а сам Enter жмём настоящей клавишей — иначе мессенджер не отправит
        if char == "\r" || char == "\n", edit.insert.last == char {
            EventPoster.perform([.delete(edit.delete), .insert(String(edit.insert.dropLast()))],
                                store: store, layouts: layouts, proxy: proxy)
            // Shift+Enter в мессенджерах — новая строка, а не отправка: Shift сохраняем
            EventPoster.postKey(CGKeyCode(keyCode), flags: flags.intersection(.maskShift), proxy: proxy)
            return nil
        }
        EventPoster.perform([.delete(edit.delete), .insert(edit.insert)], store: store, layouts: layouts, proxy: proxy)
        return nil
    }

    /// Return и Enter на цифровой клавиатуре.
    static let returnKeys: Set<UInt16> = [36, 76]
    static let tabKey: UInt16 = 48

    /// Своё сочетание из настроек, если нажато оно.
    private func combo(keyCode: UInt16, flags: CGEventFlags) -> Trigger? {
        guard !KeyboardTap.recordingHotkey else { return nil }
        return combos.first { _, hotkey in
            guard case let .combo(code, command, option, control, shift) = hotkey else { return false }
            return code == keyCode && flags.contains(.maskCommand) == command && flags.contains(.maskAlternate) == option
                && flags.contains(.maskControl) == control && flags.contains(.maskShift) == shift
        }?.trigger
    }

    private func handleFlags(_ event: CGEvent) {
        let keyCode = event.getIntegerValueField(.keyboardEventKeycode)
        // каждое нажатие и отпускание модификатора приходит сюда; левый и правый различаем по keyCode
        let isDown = !modifiersDown.contains(keyCode)
        if isDown { modifiersDown.insert(keyCode) } else { modifiersDown.remove(keyCode) }
        // флаги события — истина: если модификатор не зажат, его нет и в нашем списке
        if event.flags.intersection([.maskShift, .maskAlternate, .maskControl, .maskCommand]).isEmpty { modifiersDown.removeAll() }
        let now = ProcessInfo.processInfo.systemUptime
        for i in detectors.indices {
            guard Int64(detectors[i].detector.key.keyCode) == keyCode else {
                detectors[i].detector.otherInput()
                continue
            }
            if isDown {
                detectors[i].detector.keyDown(at: now)
            } else if detectors[i].detector.keyUp(at: now), !KeyboardTap.recordingHotkey {
                let trigger = detectors[i].trigger
                // ждём, пока отпускание модификатора дойдёт до приложения, и только потом печатаем
                DispatchQueue.main.async { [self] in fire(trigger) }
            }
        }
    }

    /// Действие сейчас доступно: иначе сочетание не глотаем, пусть дойдёт до приложения.
    private func canFire(_ trigger: Trigger) -> Bool {
        guard !ContextGuard.secureInputActive else { return false }
        return trigger == .convert || store.typographyHotkeyEnabled
    }

    private func fire(_ trigger: Trigger) {
        switch trigger {
        case .convert: doubleShift()
        case .typography: typographySelection()
        }
    }

    /// Причесать выделенный текст.
    /// Двойной правый Shift по выделенному: опечатки → «ё» → запятые (если модель скачана) → типографика.
    private func typographySelection() {
        guard store.typographyHotkeyEnabled, !ContextGuard.secureInputActive else { return }
        // Фигма, выделены слои (не редактируется текст) — тексты слоёв через плагин Vallo
        if NSWorkspace.shared.frontmostApplication?.bundleIdentifier == FigmaLauncher.figmaBundleID,
           store.figmaPluginSeen, figma != nil, !ContextGuard.focusIsEditableText() {
            figmaPress()
            return
        }
        typographyOfSelectedText()
    }

    /// Выделенный текст: опечатки → «ё» → запятые → типографика.
    private func typographyOfSelectedText() {
        SelectionReader.selectedText { [self] text in
            guard let text else { return }
            // пока модель думает, человек мог уйти в другое окно или начать печатать — тогда не вставляем
            let app = NSWorkspace.shared.frontmostApplication?.processIdentifier
            let generation = inputGeneration
            let corrected = typos.correctPhrase(text)
            let typoFixed = store.yoEnabled ? yo.fixPhrase(corrected) : corrected
            // модель думает на фоне; без модели, при ошибке или дольше 3 с — без запятых
            // в Фигме регистр и точку в конце не трогаем никогда (решение автора: это вопрос редполитики макета)
            let inFigma = NSWorkspace.shared.frontmostApplication?.bundleIdentifier == FigmaLauncher.figmaBundleID
            commas.fix(typoFixed, typos: typos, capitalize: store.typoCapitalizeEnabled && !inFigma) { [self] withCommas in
                guard NSWorkspace.shared.frontmostApplication?.processIdentifier == app, inputGeneration == generation else {
                    log.debug("right shift: focus changed while waiting — skipped")
                    return
                }
                let formatted = Typographer.format(withCommas ?? typoFixed)
                log.debug("right shift: \(text.count) → \(formatted.count) chars, model=\(withCommas != nil)")
                guard formatted != text else { return }
                engine.reset()
                live.reset()
                EventPoster.replaceSelection(formatted)
            }
        }
    }

    // MARK: - Фигма

    private var figmaPressID = 0
    /// Плагин запущен нами и ещё не вышел на связь — до этого времени новые нажатия не запускают его снова.
    private var figmaLaunchingUntil: Date?
    /// Только что запущенный плагин, пришедший, пока шёл другой пакет.
    private var figmaQueuedFile: String?
    private lazy var figmaBatch = FigmaBatch(store: store, commas: commas)

    /// Двойной правый Shift на слоях Фигмы: спросить плагин файла спереди; плагина нет — запустить.
    private func figmaPress() {
        guard let figma, let file = ContextGuard.frontWindowTitle() else {
            log.debug("figma: no window title — hint")
            return FigmaLauncher.showHint()
        }
        if figmaBatch.isRunning { return log.debug("figma: batch running — press ignored") }
        // плагин уже запускается — второй ⌘/ перезапустил бы его и потерял работу
        if let until = figmaLaunchingUntil, until > Date() { return log.debug("figma: launch in flight — press ignored") }
        // плагин ещё открыт (висит окошко итога) — сразу к нему
        if figma.connectedFiles.contains(file) { return sendPress(to: file) }
        // иначе запускаем: только что запущенный плагин скажет hello(fresh), и мы пошлём ему press
        log.debug("figma: launching plugin")
        FigmaLauncher.launch()
        let deadline = Date().addingTimeInterval(5)
        figmaLaunchingUntil = deadline
        func check() {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { [self] in
                if self.figma?.connectedFiles.contains(file) == true { self.figmaLaunchingUntil = nil; return }
                if Date() < deadline { return check() }
                log.debug("figma: plugin did not connect — hint")
                FigmaLauncher.showHint()
            }
        }
        check()
    }

    private func sendPress(to file: String) {
        figmaPressID += 1
        figma?.send(.press(id: figmaPressID), toFile: file)
    }

    private func listenToFigma() {
        figma?.onMessage = { [weak self] message, file in self?.figmaMessage(message, from: file) }
    }

    private func figmaMessage(_ message: FigmaMessage, from file: String) {
        // плагин только что запущен (нами по сочетанию или человеком из меню) — сразу за работу
        if case .hello(_, _, true) = message {
            figmaLaunchingUntil = nil
            // свитчер занят другим пакетом — этот файл обслужим следом (плагин ждёт и пишет «занят», а не «не запущен»)
            if figmaBatch.isRunning { figmaQueuedFile = file } else { sendPress(to: file) }
            return
        }
        guard case let .mode(id, mode, skipped) = message, id == figmaPressID else { return }
        switch mode {
        case .text:
            log.debug("figma: plugin says text editing — selected text path")
            typographyOfSelectedText()
        case .empty:
            break
        case .layers(let texts):
            figmaBatch.run(texts, typos: typos, yo: yo) { [weak self] edits, stats in
                guard let self else { return }
                self.figma?.send(.result(id: id, edits: edits, stats: stats, skipped: skipped), toFile: file)
                if let queued = self.figmaQueuedFile {
                    self.figmaQueuedFile = nil
                    if self.figma?.connectedFiles.contains(queued) == true { self.sendPress(to: queued) }
                }
            }
        }
    }

    private func doubleShift() {
        // пока Мак закрыл клавиатуру (поле пароля), ничего не делаем
        guard !ContextGuard.secureInputActive else { return }
        // откат важнее выделения; выделение читаем, только если откатывать нечего
        // есть откат или свежий набор — выделения быть не может, Cmd+C не нужен
        // (в VS Code Cmd+C без выделения копирует всю строку)
        if engine.canUndo || engine.hasTypedText {
            run(engine.handleDoubleShift(selection: nil))
        } else {
            SelectionReader.selectedText { [self] selection in run(engine.handleDoubleShift(selection: selection)) }
        }
    }

    private func run(_ actions: [Action]) {
        log.debug("double shift → \(actions.summary)")
        guard !actions.isEmpty else { return }
        // текст на экране переписан целиком — контекст типографики больше не верен
        live.reset()
        report()
        EventPoster.perform(actions, store: store, layouts: layouts)
    }

    private func report() {
        if let decision = engine.lastDecision { onDecision?(decision) }
    }
}
