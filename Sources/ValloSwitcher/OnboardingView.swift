import AppKit
import SwiftUI
import ValloCore

/// Тексты онбординга — через нашу же типографику.
private func T(_ s: String) -> String { Typographer.format(s) }

/// Шаги онбординга и состояние разрешений; разрешения проверяются сами, шаг перелистывается.
final class OnboardingModel: ObservableObject {
    enum Step: Int, CaseIterable { case welcome, accessibility, inputMonitoring, done }

    @Published var step: Step = .welcome
    /// Предпросмотр (запуск с `--onboarding`): шаги листаются кнопкой, а не по разрешениям.
    var preview = false
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

    /// Первый шаг, который ещё не сделан.
    func next() {
        if preview {
            step = Step(rawValue: step.rawValue + 1) ?? .done
            return
        }
        if !accessibility { step = .accessibility } else if !inputMonitoring { step = .inputMonitoring } else { step = .done }
    }

    private func refresh() {
        let wasGranted = allGranted
        accessibility = Permissions.accessibility
        inputMonitoring = Permissions.inputMonitoring
        // разрешение появилось — перелистываем шаг сам
        if !preview, (step == .accessibility && accessibility) || (step == .inputMonitoring && inputMonitoring) {
            withAnimation { next() }
        }
        if allGranted && !wasGranted { onGranted?() }
    }
}

struct OnboardingView: View {
    @ObservedObject var model: OnboardingModel
    var close: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            StepDots(current: model.step)
                .padding(.top, 20)
            Group {
                switch model.step {
                case .welcome: WelcomeStep(start: { withAnimation { model.next() } })
                case .accessibility:
                    PermissionStep(number: 1, listName: Permissions.accessibilityName,
                                   why: "Чтобы исправлять текст: стирать неправильное и печатать правильное.",
                                   open: { Permissions.open(.accessibility) },
                                   skip: model.preview ? { withAnimation { model.next() } } : nil)
                case .inputMonitoring:
                    PermissionStep(number: 2, listName: "Мониторинг ввода",
                                   why: "Чтобы видеть, что ты печатаешь, и ловить двойной Shift. Текст никуда не сохраняется и не отправляется.",
                                   open: { Permissions.open(.inputMonitoring) },
                                   skip: model.preview ? { withAnimation { model.next() } } : nil)
                case .done: DoneStep(close: close)
                }
            }
            .padding(28)
            .transition(.asymmetric(insertion: .move(edge: .trailing).combined(with: .opacity), removal: .opacity))
        }
        .frame(width: 540)
    }
}

/// Точки прогресса над шагом.
private struct StepDots: View {
    let current: OnboardingModel.Step
    var body: some View {
        HStack(spacing: 8) {
            ForEach(OnboardingModel.Step.allCases, id: \.self) { step in
                Capsule()
                    .fill(step == current ? Color.accentColor : Color.secondary.opacity(0.3))
                    .frame(width: step == current ? 22 : 8, height: 8)
            }
        }
        .animation(.easeInOut, value: current)
    }
}

private struct WelcomeStep: View {
    let start: () -> Void
    var body: some View {
        VStack(spacing: 18) {
            Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 96, height: 96)
            Text("Vallo Switcher").font(.largeTitle.bold())
            VStack(alignment: .leading, spacing: 10) {
                Feature(symbol: "character.cursor.ibeam", text: "Исправляет текст, набранный не в той раскладке: ghbdtn → привет")
                Feature(symbol: "textformat", text: "Ставит правильные тире, «ёлочки» и неразрывные пробелы")
                Feature(symbol: "lock.shield", text: "Работает без интернета. Набранный текст никуда не сохраняется")
            }
            Text(T("Осталось дать два разрешения — это минута."))
                .foregroundStyle(.secondary)
            Button(action: start) { Text("Начать").frame(minWidth: 120) }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .keyboardShortcut(.defaultAction)
        }
    }
}

private struct Feature: View {
    let symbol: String
    let text: String
    var body: some View {
        Label {
            Text(T(text))
        } icon: {
            Image(systemName: symbol).foregroundStyle(Color.accentColor).frame(width: 24)
        }
    }
}

/// Шаг с разрешением: путь, схема окна настроек, кнопка.
private struct PermissionStep: View {
    let number: Int
    let listName: String
    let why: String
    let open: () -> Void
    /// Только в предпросмотре — «Дальше».
    var skip: (() -> Void)?

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Шаг \(number) из 2").font(.callout).foregroundStyle(.secondary)
            Text(T("Включи Vallo Switcher в «\(listName)»")).font(.title2.bold())
                .fixedSize(horizontal: false, vertical: true)
            Text(T(why)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)

            SettingsSketch(listName: listName)

            VStack(alignment: .leading, spacing: 6) {
                Text(T("Системные настройки → Конфиденциальность и безопасность → \(listName)"))
                    .font(.callout.weight(.medium))
                Text(T("Нет Vallo Switcher в списке — нажми «+» внизу и выбери его в «Программах». Есть, но шаг не перелистывается — удали его «−» и добавь снова."))
                    .font(.callout).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            HStack {
                ProgressView().controlSize(.small)
                Text(T("Жду, пока включишь…")).font(.callout).foregroundStyle(.secondary)
                Spacer()
                if let skip { Button("Дальше", action: skip) }
                Button(action: open) { Text("Открыть настройки").frame(minWidth: 140) }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    .keyboardShortcut(.defaultAction)
            }
        }
    }
}

/// Схема окна «Системные настройки»: слева «Конфиденциальность и безопасность», справа список,
/// в нём Vallo Switcher с включённым переключателем. Чужих приложений нет — серые заглушки.
private struct SettingsSketch: View {
    let listName: String
    @State private var on = false

    var body: some View {
        HStack(spacing: 0) {
            // боковая панель
            VStack(alignment: .leading, spacing: 7) {
                HStack(spacing: 5) {
                    ForEach([Color.red, .yellow, .green], id: \.self) { Circle().fill($0.opacity(0.8)).frame(width: 8, height: 8) }
                }
                .padding(.bottom, 6)
                ForEach(0..<3, id: \.self) { _ in Placeholder(width: 70) }
                HStack(spacing: 5) {
                    Image(systemName: "hand.raised.fill").font(.system(size: 9)).foregroundStyle(.white)
                        .frame(width: 14, height: 14).background(RoundedRectangle(cornerRadius: 3).fill(.blue))
                    Text(T("Конфиденциальность и безопасность")).font(.system(size: 9, weight: .semibold))
                        .foregroundStyle(.white).lineLimit(2)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .padding(5)
                .background(RoundedRectangle(cornerRadius: 5).fill(Color.accentColor))
                ForEach(0..<2, id: \.self) { _ in Placeholder(width: 60) }
                Spacer()
            }
            .padding(10)
            // «Конфиденциальность» должна влезать целиком, без разрыва слова
            .frame(width: 185)
            .background(Color.secondary.opacity(0.08))

            // правая часть
            VStack(alignment: .leading, spacing: 8) {
                Text(T(listName)).font(.system(size: 12, weight: .bold))
                VStack(spacing: 0) {
                    SketchRow(content: AnyView(Placeholder(width: 90)), on: true)
                    Divider()
                    SketchRow(content: AnyView(Placeholder(width: 70)), on: false)
                    Divider()
                    SketchRow(content: AnyView(
                        HStack(spacing: 6) {
                            Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 16, height: 16)
                            Text("Vallo Switcher").font(.system(size: 11, weight: .semibold))
                        }), on: on, highlighted: true)
                }
                .background(RoundedRectangle(cornerRadius: 8).fill(Color.secondary.opacity(0.08)))
                Text("+  −").font(.system(size: 11)).foregroundStyle(.secondary)
                Spacer()
            }
            .padding(12)
        }
        .frame(height: 170)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color(nsColor: .windowBackgroundColor)))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.secondary.opacity(0.25)))
        .clipShape(RoundedRectangle(cornerRadius: 12))
        .onAppear {
            // переключатель у Vallo Switcher «включается» — показываем, что сделать
            on = false
            withAnimation(.easeInOut(duration: 0.4).delay(0.8).repeatForever(autoreverses: true)) { on = true }
        }
    }
}

private struct SketchRow: View {
    let content: AnyView
    let on: Bool
    var highlighted = false
    var body: some View {
        HStack {
            content
            Spacer()
            Capsule().fill(on ? Color.accentColor : Color.secondary.opacity(0.3))
                .frame(width: 26, height: 15)
                .overlay(Circle().fill(.white).padding(2).offset(x: on ? 5.5 : -5.5))
        }
        .padding(.horizontal, 8).padding(.vertical, 6)
        .background(highlighted ? Color.accentColor.opacity(0.12) : Color.clear)
    }
}

private struct Placeholder: View {
    let width: CGFloat
    var body: some View {
        RoundedRectangle(cornerRadius: 3).fill(Color.secondary.opacity(0.25)).frame(width: width, height: 8)
    }
}

private struct DoneStep: View {
    let close: () -> Void
    var body: some View {
        VStack(spacing: 18) {
            Image(systemName: "checkmark.circle.fill").font(.system(size: 56)).foregroundStyle(.green)
            Text("Готово, свитчер работает").font(.title.bold())
            Text(T("Попробуй: в английской раскладке набери ghbdtn — получится «привет»."))
                .multilineTextAlignment(.center)
            VStack(alignment: .leading, spacing: 10) {
                Feature(symbol: "shift", text: "Дважды левый Shift — откатить исправление или перевести выделенное")
                Feature(symbol: "shift.fill", text: "Дважды правый Shift — причесать типографику в выделенном тексте")
                Feature(symbol: "menubar.rectangle", text: "Иконка ↩ в строке меню — настройки, исключения, сочетания")
            }
            Button(action: close) { Text("Закрыть").frame(minWidth: 120) }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .keyboardShortcut(.defaultAction)
        }
    }
}

/// Окно онбординга: при первом запуске и из меню.
final class OnboardingWindow {
    let model = OnboardingModel()
    private var window: NSWindow?

    func show(preview: Bool = false) {
        model.startWatching()
        model.preview = preview
        model.step = model.allGranted && !preview ? .done : .welcome
        if window == nil {
            let view = OnboardingView(model: model) { [weak self] in self?.close() }
            let host = NSHostingController(rootView: view)
            host.sizingOptions = .preferredContentSize
            let w = NSWindow(contentViewController: host)
            w.title = "Vallo Switcher"
            w.styleMask = [.titled, .closable, .fullSizeContentView]
            w.titlebarAppearsTransparent = true
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
