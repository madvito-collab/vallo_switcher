import AppKit
import CoreGraphics
import ValloCore

/// «Уши»: перехват клавиатуры и мыши во всех приложениях.
final class KeyboardTap {
    static let leftShiftKey: Int64 = 56

    let engine: Engine
    let store: SettingsStore
    let layouts: SystemLayouts
    /// Вызывается после каждого решения движка — для меню отладки.
    var onDecision: ((String) -> Void)?

    private var detector = DoubleShiftDetector()
    private var shiftDown = false
    private var tap: CFMachPort?

    init(engine: Engine, store: SettingsStore, layouts: SystemLayouts) {
        self.engine = engine
        self.store = store
        self.layouts = layouts
    }

    func start() -> Bool {
        let mask: CGEventMask = [CGEventType.keyDown, .flagsChanged, .leftMouseDown, .rightMouseDown, .otherMouseDown]
            .reduce(0) { $0 | (1 << $1.rawValue) }
        let callback: CGEventTapCallBack = { _, type, event, info in
            let me = Unmanaged<KeyboardTap>.fromOpaque(info!).takeUnretainedValue()
            return me.handle(type: type, event: event)
        }
        guard let tap = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap,
                                          eventsOfInterest: mask, callback: callback,
                                          userInfo: Unmanaged.passUnretained(self).toOpaque()) else { return false }
        self.tap = tap
        SelectionReader.enableAccessibilityInFrontApp()
        let source = CFMachPortCreateRunLoopSource(nil, tap, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)

        NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main
        ) { [weak self] _ in
            log.debug("app activated → reset")
            self?.engine.reset()
            // Chrome и Electron отдают поля через Accessibility, только если попросить, — просим сразу,
            // иначе охранник не видит, что курсор в поле e-mail или телефона
            SelectionReader.enableAccessibilityInFrontApp()
        }
        return true
    }

    private func handle(type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let tap { CGEvent.tapEnable(tap: tap, enable: true) }
            return Unmanaged.passUnretained(event)
        }
        if event.getIntegerValueField(.eventSourceUserData) == EventPoster.marker {
            return Unmanaged.passUnretained(event)
        }
        switch type {
        case .keyDown:
            return handleKeyDown(event)
        case .flagsChanged:
            handleFlags(event)
        default:
            log.debug("mouse → reset")
            detector.otherInput()
            engine.reset()
        }
        return Unmanaged.passUnretained(event)
    }

    private func handleKeyDown(_ event: CGEvent) -> Unmanaged<CGEvent>? {
        detector.otherInput()
        let flags = event.flags
        let stroke = KeyStroke(keyCode: UInt16(event.getIntegerValueField(.keyboardEventKeycode)),
                               shift: flags.contains(.maskShift),
                               capsLock: flags.contains(.maskAlphaShift))
        let modified = !flags.intersection([.maskCommand, .maskControl, .maskAlternate]).isEmpty
        if store.debugMode { ContextGuard.dumpFocusedElement() }
        engine.autoSwitchEnabled = store.autoSwitchEnabled && ContextGuard.autoSwitchAllowed(store: store)
        // уведомления о смене раскладки иногда не приходят — сверяемся с Маком перед каждой клавишей
        let actual = layouts.current
        if actual != engine.currentLanguage {
            log.debug("layout drift: engine=\(self.engine.currentLanguage.rawValue, privacy: .public) actual=\(actual.rawValue, privacy: .public)")
            engine.externalLayoutChange(to: actual)
        }
        let output = engine.handleKey(KeyEvent(stroke: stroke, hasCommandControlOrOption: modified))
        log.debug("key \(stroke.keyCode, privacy: .public) → \(String(describing: output), privacy: .public)")
        guard case .consume(let actions) = output else { return Unmanaged.passUnretained(event) }
        report()
        EventPoster.perform(actions, store: store, layouts: layouts)
        return nil
    }

    private func handleFlags(_ event: CGEvent) {
        let keyCode = event.getIntegerValueField(.keyboardEventKeycode)
        guard keyCode == KeyboardTap.leftShiftKey else {
            detector.otherInput()
            return
        }
        let now = ProcessInfo.processInfo.systemUptime
        let isDown = event.flags.contains(.maskShift)
        if isDown && !shiftDown {
            detector.leftShiftDown(at: now)
        } else if !isDown && shiftDown, detector.leftShiftUp(at: now) {
            // ждём, пока отпускание Shift дойдёт до приложения, и только потом печатаем
            DispatchQueue.main.async { [self] in doubleShift() }
        }
        shiftDown = isDown
    }

    private func doubleShift() {
        // пока Мак закрыл клавиатуру (поле пароля), ничего не делаем
        guard !ContextGuard.secureInputActive else { return }
        // откат важнее выделения; выделение читаем, только если откатывать нечего
        if engine.canUndo {
            run(engine.handleDoubleShift(selection: nil))
        } else {
            SelectionReader.selectedText { [self] selection in run(engine.handleDoubleShift(selection: selection)) }
        }
    }

    private func run(_ actions: [Action]) {
        log.debug("double shift → \(String(describing: actions), privacy: .public)")
        guard !actions.isEmpty else { return }
        report()
        EventPoster.perform(actions, store: store, layouts: layouts)
    }

    private func report() {
        if let decision = engine.lastDecision { onDecision?(decision) }
    }
}
