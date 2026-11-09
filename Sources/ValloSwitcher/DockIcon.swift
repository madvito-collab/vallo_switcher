import AppKit

/// Значок в Dock — пока открыто окно настроек или онбординга: свитчер живёт в строке меню, а на Маке
/// с чёлкой его значок там может спрятаться. Через Dock окно всегда можно вернуть.
enum DockIcon {
    static func show() {
        NSApp.setActivationPolicy(.regular)
    }

    /// Закрылось наше окно — убрать значок, если других открытых окон не осталось.
    static func hideIfNoWindows() {
        // окно ещё числится видимым, пока закрывается — смотрим на следующем витке
        DispatchQueue.main.async {
            let open = NSApp.windows.contains { $0.isVisible && $0.styleMask.contains(.titled) }
            if !open { NSApp.setActivationPolicy(.accessory) }
        }
    }
}
