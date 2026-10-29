import AppKit
import ApplicationServices
import Carbon

/// «Охранник»: можно ли сейчас автопереключать.
enum ContextGuard {
    /// Типы полей на сайтах, где автопереключение мешает.
    static let blockedInputTypes: Set<String> = ["password", "email", "tel", "url"]

    /// Мак закрыл клавиатуру от всех программ (активно поле пароля где-то в системе).
    static var secureInputActive: Bool { IsSecureEventInputEnabled() }

    static func autoSwitchAllowed(store: SettingsStore) -> Bool {
        if let id = NSWorkspace.shared.frontmostApplication?.bundleIdentifier,
           store.excludedBundleIDs.contains(id) { return false }
        if secureInputActive { return false }
        guard let element = focusedElement() else { return true }
        if string(element, kAXSubroleAttribute) == kAXSecureTextFieldSubrole { return false }
        if let type = inputType(of: element), blockedInputTypes.contains(type) { return false }
        return true
    }

    private static func focusedElement() -> AXUIElement? {
        var focused: CFTypeRef?
        guard AXUIElementCopyAttributeValue(AXUIElementCreateSystemWide(), kAXFocusedUIElementAttribute as CFString,
                                            &focused) == .success, let focused else { return nil }
        return (focused as! AXUIElement)
    }

    /// Тип поля на сайте. Отдельного атрибута браузеры не дают (разведка Task 9), но пишут тип
    /// в описание поля на языке системы: «поле e-mail», «поле номера телефона», «Поле URL».
    private static func inputType(of element: AXUIElement) -> String? {
        guard let description = string(element, kAXRoleDescriptionAttribute)?.lowercased() else { return nil }
        if description.contains("mail") { return "email" }
        if description.contains("телефон") || description.contains("phone") { return "tel" }
        if description.contains("url") { return "url" }
        if description.contains("парол") || description.contains("password") { return "password" }
        return nil
    }

    private static func string(_ element: AXUIElement, _ attribute: String) -> String? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute as CFString, &value) == .success else { return nil }
        return value as? String
    }

    /// Разведка: все атрибуты фокусного поля в журнал (только в режиме отладки).
    static func dumpFocusedElement() {
        guard let element = focusedElement() else { return }
        var names: CFArray?
        AXUIElementCopyAttributeNames(element, &names)
        let list = (names as? [String]) ?? []
        let values = list.map { name -> String in
            var v: CFTypeRef?
            AXUIElementCopyAttributeValue(element, name as CFString, &v)
            if let s = v as? String { return "\(name)=\(s.prefix(40))" }
            if let a = v as? [Any] { return "\(name)=[\(a.prefix(5).map { "\($0)" }.joined(separator: ","))]" }
            return name
        }
        log.debug("AX focused: \(values.joined(separator: " | "), privacy: .public)")
    }
}
