import AppKit

/// «Лицо»: иконка в строке меню и её меню.
final class StatusMenu: NSObject {
    enum State { case working, needsPermissions, blocked }

    private let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
    private let store: SettingsStore
    private let autoItem = NSMenuItem(title: "Автоматически переключать раскладку", action: #selector(toggleAuto), keyEquivalent: "")
    private let permissionsItem = NSMenuItem(title: "Дать разрешения…", action: #selector(givePermissions), keyEquivalent: "")
    private let debugHeader = NSMenuItem(title: "Последние решения:", action: nil, keyEquivalent: "")
    private var decisions: [String] = []
    private let menu = NSMenu()

    var onOpenSettings: (() -> Void)?
    var onGivePermissions: (() -> Void)?

    /// Иконка ученика (шаблон: Мак сам красит её под светлую и тёмную строку меню).
    private static let icon: NSImage? = {
        // PNG 1x/2x, а не SVG: SVG старые macOS могут прочитать «пустым» (scripts/render-menubar-icon.swift)
        guard let image = Bundle.main.image(forResource: "MenuBarIcon") else { return nil }
        image.isTemplate = true
        image.size = NSSize(width: 18, height: 18)
        return image
    }()

    init(store: SettingsStore) {
        self.store = store
        super.init()
        autoItem.target = self
        permissionsItem.target = self
        let settings = NSMenuItem(title: "Настройки…", action: #selector(openSettings), keyEquivalent: ",")
        settings.target = self
        menu.addItem(permissionsItem)
        menu.addItem(autoItem)
        menu.addItem(.separator())
        menu.addItem(settings)
        menu.addItem(NSMenuItem(title: "Выйти", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
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

    @objc private func toggleAuto() { store.autoSwitchEnabled.toggle() }
    @objc private func givePermissions() { onGivePermissions?() }
    @objc private func openSettings() { onOpenSettings?() }
}

extension StatusMenu: NSMenuDelegate {
    func menuWillOpen(_ menu: NSMenu) {
        autoItem.state = store.autoSwitchEnabled ? .on : .off
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
