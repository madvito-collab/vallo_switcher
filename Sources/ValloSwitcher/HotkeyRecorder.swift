import AppKit
import ValloCore

/// Запись «своего варианта»: ловит первое нажатие с Cmd, Option или Control в окне настроек.
/// Esc — отмена.
final class HotkeyRecorder {
    private var monitor: Any?

    func start(completion: @escaping (Hotkey?) -> Void) {
        stop()
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if event.keyCode == 53 {   // Esc
                self?.stop()
                completion(nil)
                return nil
            }
            let f = event.modifierFlags
            // без Cmd/Option/Control сочетание перехватывало бы обычные буквы
            guard f.contains(.command) || f.contains(.option) || f.contains(.control) else { return nil }
            self?.stop()
            completion(.combo(keyCode: event.keyCode, command: f.contains(.command), option: f.contains(.option),
                              control: f.contains(.control), shift: f.contains(.shift)))
            return nil
        }
    }

    func stop() {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
    }
}
