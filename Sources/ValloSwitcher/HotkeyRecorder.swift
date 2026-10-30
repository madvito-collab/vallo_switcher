import AppKit
import ValloCore

/// Запись «своего варианта»: ловит первое нажатие с Cmd, Option или Control в окне настроек.
/// Esc — отмена.
final class HotkeyRecorder {
    private var monitor: Any?

    /// Стандартные ⌘-сочетания — их нельзя забирать у всех приложений.
    static let reserved: Set<UInt16> = [8, 9, 7, 6, 0, 12, 13, 1, 17, 45, 3, 35, 4, 46, 31, 48, 49]   // C V X Z A Q W S T N F P H M O Tab Space

    func start(completion: @escaping (Hotkey?) -> Void) {
        stop()
        KeyboardTap.recordingHotkey = true
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if event.keyCode == 53 {   // Esc
                self?.stop()
                completion(nil)
                return nil
            }
            let f = event.modifierFlags
            // без Cmd/Option/Control сочетание перехватывало бы обычные буквы
            guard f.contains(.command) || f.contains(.option) || f.contains(.control) else { return nil }
            // ⌘C, ⌘V и т. п. без ⌥/⌃ — системные, не берём, ждём другое сочетание
            if f.contains(.command), !f.contains(.option), !f.contains(.control),
               HotkeyRecorder.reserved.contains(event.keyCode) { return nil }
            self?.stop()
            completion(.combo(keyCode: event.keyCode, command: f.contains(.command), option: f.contains(.option),
                              control: f.contains(.control), shift: f.contains(.shift)))
            return nil
        }
    }

    func stop() {
        KeyboardTap.recordingHotkey = false
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
    }
}
