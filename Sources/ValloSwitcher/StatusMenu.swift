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
    var onGivePermissions: (() -> Void)?
    /// «Скачать модель запятых» — открыть вкладку «Опечатки» и начать скачивание.
    var onDownloadModel: (() -> Void)?

    /// Иконка ученика (шаблон: Мак сам красит её под светлую и тёмную строку меню).
    private static let icon: NSImage? = {
        // PNG 1x/2x, а не SVG: SVG старые macOS могут прочитать «пустым» (scripts/render-menubar-icon.swift)
        guard let image = Bundle.main.image(forResource: "MenuBarIcon") else { return nil }
        image.isTemplate = true
        image.size = NSSize(width: 18, height: 18)
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
        // и текст пунктов поедет по вертикали
        let settings = NSMenuItem(title: "Настройки…", action: #selector(openSettings), keyEquivalent: "")
        settings.target = self
        let help = NSMenuItem(title: "Справка", action: #selector(openHelp), keyEquivalent: "")
        help.target = self
        let quit = NSMenuItem(title: "Выйти из Vallo Switcher", action: #selector(quit), keyEquivalent: "")
        quit.target = self
        menu.addItem(autoItem)
        menu.addItem(.separator())
        menu.addItem(permissionsItem)
        menu.addItem(modelItem)
        menu.addItem(settings)
        menu.addItem(help)
        menu.addItem(.separator())
        menu.addItem(quit)
        menu.delegate = self
        item.menu = menu
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
    @objc private func downloadModel() { onDownloadModel?() }
    @objc private func quit() { NSApp.terminate(nil) }
    @objc private func openHelp() {
        if let url = Bundle.main.url(forResource: "Help", withExtension: "html") { NSWorkspace.shared.open(url) }
    }
}

/// Строка меню «название слева — переключатель справа», как «Wi-Fi» в меню Wi-Fi.
final class SwitchRowView: NSView {
    private let label = NSTextField(labelWithString: "")
    private let toggle = NSSwitch()
    private let onChange: (Bool) -> Void

    init(title: String, onChange: @escaping (Bool) -> Void) {
        self.onChange = onChange
        super.init(frame: NSRect(x: 0, y: 0, width: 260, height: 34))
        label.stringValue = title
        label.font = .boldSystemFont(ofSize: NSFont.systemFontSize)
        toggle.controlSize = .small
        toggle.target = self
        toggle.action = #selector(changed)
        for v in [label, toggle] as [NSView] {
            v.translatesAutoresizingMaskIntoConstraints = false
            addSubview(v)
        }
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 14),
            label.centerYAnchor.constraint(equalTo: centerYAnchor),
            toggle.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -14),
            toggle.centerYAnchor.constraint(equalTo: centerYAnchor),
            label.trailingAnchor.constraint(lessThanOrEqualTo: toggle.leadingAnchor, constant: -12),
        ])
        autoresizingMask = [.width]
    }

    required init?(coder: NSCoder) { fatalError() }

    var isOn: Bool {
        get { toggle.state == .on }
        set { toggle.state = newValue ? .on : .off }
    }

    @objc private func changed() { onChange(isOn) }
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
