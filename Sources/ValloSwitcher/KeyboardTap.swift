import AppKit
import CoreGraphics
import ValloCore

/// «Уши»: перехват клавиатуры и мыши во всех приложениях.
final class KeyboardTap {
    /// Что делать по сочетанию.
    enum Trigger { case convert, typography }

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
    private var tap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?

    init(engine: Engine, store: SettingsStore, layouts: SystemLayouts) {
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
        if let trigger = combo(keyCode: keyCode, flags: flags) {
            DispatchQueue.main.async { [self] in fire(trigger) }
            return nil
        }
        let stroke = KeyStroke(keyCode: keyCode, shift: flags.contains(.maskShift), capsLock: flags.contains(.maskAlphaShift))
        let modified = !flags.intersection([.maskCommand, .maskControl, .maskAlternate]).isEmpty
        if store.debugMode { ContextGuard.dumpFocusedElement() }
        engine.autoSwitchEnabled = store.autoSwitchEnabled && ContextGuard.autoSwitchAllowed(store: store)
        let liveAllowed = store.typographyLiveEnabled && ContextGuard.typographyAllowed(store: store)

        // Backspace сразу после типографской правки — вернуть набранное
        if keyCode == Engine.backspaceKey && !modified {
            if liveAllowed, let edit = live.backspace() {
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
            // сначала свитчер; на этом нажатии типографика молчит
            live.reset()
            report()
            // через proxy: наши события встают сразу за текущей клавишей, раньше следующих нажатий
            EventPoster.perform(actions, store: store, layouts: layouts, proxy: proxy)
            return nil
        }

        // типографика на лету
        guard keyCode != Engine.backspaceKey else { return Unmanaged.passUnretained(event) }
        guard !modified, liveAllowed,
              let text = layouts.pair.layout(actual).character(for: stroke), text.count == 1, let char = text.first else {
            live.reset()
            return Unmanaged.passUnretained(event)
        }
        guard let edit = live.typed(char, hint: actual) else { return Unmanaged.passUnretained(event) }
        log.debug("typography → delete(\(edit.delete)) insert(\(edit.insert.count) chars)")
        // длина текста изменилась (… вместо ...) — память свитчера больше не совпадает с экраном
        if live.lastEditChangedLength { engine.reset() }
        EventPoster.perform([.delete(edit.delete), .insert(edit.insert)], store: store, layouts: layouts, proxy: proxy)
        return nil
    }

    /// Своё сочетание из настроек, если нажато оно.
    private func combo(keyCode: UInt16, flags: CGEventFlags) -> Trigger? {
        combos.first { _, hotkey in
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
            } else if detectors[i].detector.keyUp(at: now) {
                let trigger = detectors[i].trigger
                // ждём, пока отпускание модификатора дойдёт до приложения, и только потом печатаем
                DispatchQueue.main.async { [self] in fire(trigger) }
            }
        }
    }

    private func fire(_ trigger: Trigger) {
        switch trigger {
        case .convert: doubleShift()
        case .typography: typographySelection()
        }
    }

    /// Причесать выделенный текст.
    private func typographySelection() {
        guard store.typographyHotkeyEnabled, !ContextGuard.secureInputActive else { return }
        SelectionReader.selectedText { [self] text in
            guard let text else { return }
            let formatted = Typographer.format(text)
            log.debug("typography selection: \(text.count) → \(formatted.count) chars")
            guard formatted != text else { return }
            engine.reset()
            live.reset()
            EventPoster.replaceSelection(formatted)
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
        report()
        EventPoster.perform(actions, store: store, layouts: layouts)
    }

    private func report() {
        if let decision = engine.lastDecision { onDecision?(decision) }
    }
}
