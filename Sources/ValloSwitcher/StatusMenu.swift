import AppKit

/// «Лицо»: иконка в строке меню и её меню.
final class StatusMenu: NSObject {
    enum State { case working, needsPermissions, blocked }

    private let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private let store: SettingsStore
    private let modelStore: ModelStore
    /// Верхняя строка «Автопереключение» с переключателем справа — как «Wi-Fi» в меню Wi-Fi.
    private lazy var autoRow = SwitchRowView(title: "Автопереключение") { [weak self] isOn in
        self?.store.autoSwitchEnabled = isOn
    }
    private let permissionsItem = NSMenuItem(title: "Дать разрешения…", action: #selector(givePermissions), keyEquivalent: "")
    private let modelItem = NSMenuItem(title: "", action: #selector(downloadModel), keyEquivalent: "")
    private let debugHeader = NSMenuItem(title: "Последние решения:", action: nil, keyEquivalent: "")
    private var decisions: [String] = []
    private let menu = NSMenu()

    var onOpenSettings: (() -> Void)?
    /// Справка и «О программе» — страницы окна настроек.
    var onOpenHelp: (() -> Void)?
    var onOpenAbout: (() -> Void)?
    var onCheckUpdates: (() -> Void)?
    /// Нажали пункт про новую версию.
    var onInstallUpdate: (() -> Void)?
    /// Пункт «Перезапустить и обновить до …» / «Обновить до …» — виден, пока есть новая версия.
    private let updateItem = NSMenuItem(title: "", action: #selector(installUpdate), keyEquivalent: "")
    /// Точка-бейдж на значке: вышла новая версия.
    private let badge: NSView = {
        let v = NSView(frame: NSRect(x: 0, y: 0, width: 7, height: 7))
        v.wantsLayer = true
        v.layer?.backgroundColor = NSColor.systemRed.cgColor
        v.layer?.cornerRadius = 3.5
        v.isHidden = true
        return v
    }()
    var onGivePermissions: (() -> Void)?
    /// «Скачать модель запятых» — открыть «Исправления» и начать скачивание.
    var onDownloadModel: (() -> Void)?

    /// Иконка автора (шаблон: Мак сам красит её под светлую и тёмную строку меню).
    private static let icon: NSImage? = {
        // PNG 1x/2x из набора автора (Support/Icons, «ё» 16/32 px): SVG старые macOS могут прочитать «пустым»
        guard let image = Bundle.main.image(forResource: "MenuBarIcon") else { return nil }
        image.isTemplate = true
        image.size = NSSize(width: 16, height: 16)
        return image
    }()

    init(store: SettingsStore, modelStore: ModelStore) {
        self.store = store
        self.modelStore = modelStore
        super.init()
        let autoItem = NSMenuItem()
        autoItem.view = autoRow
        permissionsItem.target = self
        modelItem.target = self
        // без сочетаний и стандартных названий-действий: иначе macOS сама дорисует иконки,
        // и текст пунктов поедет по вертикали (в macOS 27 иконки ещё и прячем явно — см. ниже)
        let settings = NSMenuItem(title: "Настройки…", action: #selector(openSettings), keyEquivalent: "")
        settings.target = self
        let updates = NSMenuItem(title: "Проверить обновления…", action: #selector(checkUpdates), keyEquivalent: "")
        updates.target = self
        let help = NSMenuItem(title: "Справка", action: #selector(openHelp), keyEquivalent: "")
        help.target = self
        let about = NSMenuItem(title: "О программе", action: #selector(openAbout), keyEquivalent: "")
        about.target = self
        let quit = NSMenuItem(title: "Завершить Vallo Switcher", action: #selector(quit), keyEquivalent: "")
        quit.target = self
        // три группы: автопереключение / настройки и обновления / справка, о программе, завершить
        updateItem.target = self
        updateItem.isHidden = true
        menu.addItem(updateItem)
        menu.addItem(autoItem)
        menu.addItem(.separator())
        menu.addItem(permissionsItem)
        menu.addItem(modelItem)
        menu.addItem(settings)
        menu.addItem(updates)
        menu.addItem(.separator())
        menu.addItem(help)
        menu.addItem(about)
        menu.addItem(quit)
        // macOS 27 сам подставляет значки к «Настройкам…», «Завершить» и т. п. (шестерёнка) — автору не нужны
        if #available(macOS 27, *) {
            for i in menu.items { i.preferredImageVisibility = .hidden }
        }
        menu.delegate = self
        item.menu = menu
        if let button = item.button {
            // правый верхний угол значка 16×16 по центру кнопки
            badge.frame.origin = NSPoint(x: button.bounds.midX + 3, y: button.bounds.midY + 2)
            badge.autoresizingMask = [.minXMargin, .maxXMargin, .minYMargin, .maxYMargin]
            button.addSubview(badge)
        }
        set(state: .working)
    }

    func set(state: State) {
        if state == .needsPermissions {
            item.button?.image = NSImage(systemSymbolName: "exclamationmark.triangle", accessibilityDescription: "Vallo Switcher")
        } else {
            item.button?.image = StatusMenu.icon
                ?? NSImage(systemSymbolName: "keyboard", accessibilityDescription: "Vallo Switcher")
        }
        item.button?.appearsDisabled = state == .blocked
        permissionsItem.isHidden = state != .needsPermissions
    }

    /// Новая версия: бейдж на значке и пункт сверху меню. nil — убрать.
    func show(update: Updater.Pending?) {
        badge.isHidden = update == nil
        updateItem.isHidden = update == nil
        switch update {
        case .ready(let version): updateItem.title = "Перезапустить и обновить до \(version)"
        case .available(let version): updateItem.title = "Обновить до \(version)…"
        case nil: break
        }
    }

    func record(decision: String) {
        decisions.insert(decision, at: 0)
        decisions = Array(decisions.prefix(10))
    }

    /// Пункт модели запятых: нет модели — предложить скачать; скачивается — показать проценты; есть — скрыть.
    private func updateModelItem() {
        switch modelStore.state {
        case .ready:
            modelItem.isHidden = true
        case .notDownloaded:
            modelItem.isHidden = false
            modelItem.isEnabled = true
            modelItem.title = "Скачать модель запятых (\(ModelStore.downloadSize))…"
        case .downloading(let progress):
            modelItem.isHidden = false
            modelItem.isEnabled = false
            modelItem.title = "Модель запятых: скачано \(Int(progress * 100)) %"
        case .failed:
            modelItem.isHidden = false
            modelItem.isEnabled = true
            modelItem.title = "Модель запятых не скачалась — повторить…"
        }
    }

    @objc private func givePermissions() { onGivePermissions?() }
    @objc private func openSettings() { onOpenSettings?() }
    @objc private func checkUpdates() { onCheckUpdates?() }
    @objc private func installUpdate() { onInstallUpdate?() }
    @objc private func openAbout() { onOpenAbout?() }
    @objc private func downloadModel() { onDownloadModel?() }
    @objc private func quit() { NSApp.terminate(nil) }
    @objc private func openHelp() { onOpenHelp?() }
}

/// Строка меню «название слева — переключатель справа», как «Wi-Fi» в меню Wi-Fi.
final class SwitchRowView: NSView {
    private let label = NSTextField(labelWithString: "")
    private let toggle = PillSwitch()
    private let onChange: (Bool) -> Void

    init(title: String, onChange: @escaping (Bool) -> Void) {
        self.onChange = onChange
        super.init(frame: NSRect(x: 0, y: 0, width: 260, height: 34))
        label.stringValue = title
        label.font = .menuFont(ofSize: 0)
        toggle.onChange = { [weak self] in self?.onChange($0) }
        for v in [label, toggle] as [NSView] {
            v.translatesAutoresizingMaskIntoConstraints = false
            addSubview(v)
        }
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 14),
            label.centerYAnchor.constraint(equalTo: centerYAnchor),
            toggle.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -14),
            toggle.centerYAnchor.constraint(equalTo: centerYAnchor),
            toggle.widthAnchor.constraint(equalToConstant: 32),
            toggle.heightAnchor.constraint(equalToConstant: 18),
            label.trailingAnchor.constraint(lessThanOrEqualTo: toggle.leadingAnchor, constant: -12),
        ])
        autoresizingMask = [.width]
    }

    required init?(coder: NSCoder) { fatalError() }

    var isOn: Bool {
        get { toggle.isOn }
        set { toggle.isOn = newValue }
    }
}

/// Свой тумблер: стандартный NSSwitch в меню macOS рисует серым в обоих состояниях
/// (меню не считается активным окном). Включён — цвет акцента системы, выключен — серый.
final class PillSwitch: NSView {
    var onChange: ((Bool) -> Void)?
    var isOn = false { didSet { needsDisplay = true } }

    override func draw(_ dirtyRect: NSRect) {
        let r = bounds.insetBy(dx: 0.5, dy: 0.5)
        let track = NSBezierPath(roundedRect: r, xRadius: r.height / 2, yRadius: r.height / 2)
        (isOn ? NSColor.controlAccentColor : NSColor.tertiaryLabelColor).setFill()
        track.fill()
        let d = r.height - 4
        let x = isOn ? r.maxX - d - 2 : r.minX + 2
        let knob = NSBezierPath(ovalIn: NSRect(x: x, y: r.minY + 2, width: d, height: d))
        NSColor.white.setFill()
        knob.fill()
    }

    override func mouseDown(with event: NSEvent) {
        isOn.toggle()
        onChange?(isOn)
    }
}

extension StatusMenu: NSMenuDelegate {
    func menuWillOpen(_ menu: NSMenu) {
        autoRow.isOn = store.autoSwitchEnabled
        updateModelItem()
        // режим отладки: последние решения судьи в конце меню
        while let last = menu.items.last, last.tag == 1 { menu.removeItem(last) }
        guard store.debugMode else { return }
        let sep = NSMenuItem.separator()
        sep.tag = 1
        menu.addItem(sep)
        debugHeader.tag = 1
        menu.addItem(debugHeader)
        for d in decisions.isEmpty ? ["пока пусто"] : decisions {
            let i = NSMenuItem(title: d, action: nil, keyEquivalent: "")
            i.tag = 1
            menu.addItem(i)
        }
    }
}
