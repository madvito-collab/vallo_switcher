import AppKit
import SwiftUI

/// Состояние разрешений для окна онбординга, обновляется само.
final class OnboardingModel: ObservableObject {
    @Published var accessibility = Permissions.accessibility
    @Published var inputMonitoring = Permissions.inputMonitoring
    private var timer: Timer?
    /// Оба разрешения появились.
    var onGranted: (() -> Void)?

    var allGranted: Bool { accessibility && inputMonitoring }

    func startWatching() {
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in self?.refresh() }
    }

    func stopWatching() {
        timer?.invalidate()
        timer = nil
    }

    private func refresh() {
        let wasGranted = allGranted
        accessibility = Permissions.accessibility
        inputMonitoring = Permissions.inputMonitoring
        if allGranted && !wasGranted { onGranted?() }
    }
}

struct OnboardingView: View {
    @ObservedObject var model: OnboardingModel
    var close: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 12) {
                Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 48, height: 48)
                VStack(alignment: .leading, spacing: 2) {
                    Text("Vallo Switcher").font(.title2.bold())
                    Text("Исправляет текст, набранный не в той раскладке").foregroundStyle(.secondary)
                }
            }

            if model.allGranted {
                VStack(alignment: .leading, spacing: 8) {
                    Label("Готово, свитчер работает", systemImage: "checkmark.circle.fill")
                        .font(.headline).foregroundStyle(.green)
                    Text("Попробуй: в английской раскладке набери ghbdtn — получится «привет».")
                    Text("Ошибся свитчер — дважды нажми левый Shift, и слово вернётся.")
                        .foregroundStyle(.secondary)
                }
                HStack {
                    Spacer()
                    Button("Закрыть", action: close).keyboardShortcut(.defaultAction)
                }
            } else {
                Text("Нужны два разрешения Мака. Набранный текст никуда не сохраняется и не отправляется.")

                PermissionRow(title: "Универсальный доступ",
                              detail: "чтобы исправлять текст: стирать и печатать заново",
                              granted: model.accessibility,
                              open: { Permissions.open(.accessibility) })
                PermissionRow(title: "Мониторинг ввода",
                              detail: "чтобы видеть набор и ловить двойной Shift",
                              granted: model.inputMonitoring,
                              open: { Permissions.open(.inputMonitoring) })

                Text("В настройках включи переключатель у Vallo Switcher. Если его нет в списке — нажми «+» и выбери его в «Программах». Если он есть, но галочка тут не появляется — удали его «−» и добавь заново.")
                    .font(.callout).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)

                HStack {
                    Spacer()
                    Button("Позже", action: close)
                }
            }
        }
        .padding(24)
        .frame(width: 460)
    }
}

private struct PermissionRow: View {
    let title: String
    let detail: String
    let granted: Bool
    let open: () -> Void

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: granted ? "checkmark.circle.fill" : "circle")
                .font(.title2)
                .foregroundStyle(granted ? .green : .secondary)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.headline)
                Text(detail).font(.callout).foregroundStyle(.secondary)
            }
            Spacer()
            if !granted { Button("Открыть настройки", action: open) }
        }
        .padding(12)
        .background(RoundedRectangle(cornerRadius: 10).fill(Color.secondary.opacity(0.08)))
    }
}

/// Окно онбординга: при первом запуске и из меню.
final class OnboardingWindow {
    let model = OnboardingModel()
    private var window: NSWindow?

    func show() {
        model.startWatching()
        if window == nil {
            let view = OnboardingView(model: model) { [weak self] in self?.close() }
            let w = NSWindow(contentViewController: NSHostingController(rootView: view))
            w.title = "Vallo Switcher"
            w.styleMask = [.titled, .closable]
            w.isReleasedWhenClosed = false
            w.center()
            window = w
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    func close() {
        model.stopWatching()
        window?.close()
    }
}
