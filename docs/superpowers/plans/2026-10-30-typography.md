# Типографика и настраиваемые сочетания — план работ

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Тире, кавычки, неразрывные пробелы и символы по правилам Лебедева: на лету и по сочетанию для выделенного текста. Плюс настраиваемые сочетания для обоих действий.

**Architecture:** Все правила живут в одной чистой функции `Typographer.format(_:final:)` в `ValloCore`. В режиме по сочетанию она применяется к выделенному тексту целиком (`final: true`). На лету та же функция применяется к последним набранным символам (`final: false`), и правила, которым нужен правый контекст, ждут следующего символа. Разница между «набрано» и «причёсано» превращается в правку: сколько стереть, что вставить, что вернуть по Backspace. Детектор двойного Shift обобщается до двойного нажатия любого модификатора и плюс к нему «своё сочетание».

**Tech Stack:** Swift 6, SwiftPM, Swift Testing, AppKit/SwiftUI; те же цели `ValloCore` / `ValloSwitcher`.

**Spec:** `docs/superpowers/specs/2026-10-30-typography-design.md` (основа — `2026-10-29-vallo-switcher-design.md`)

## Global Constraints

- Символы: тире «—» U+2014, короткое тире «–» U+2013, минус «−» U+2212, неразрывный пробел U+00A0, многоточие «…» U+2026, ёлочки «» U+00AB/U+00BB, лапки „“ U+201E/U+201C, английские “” U+201C/U+201D и ‘’ U+2018/U+2019.
- Предлоги и союзы, после которых ставится U+00A0: `а в во и к ко о об от с со у на по за до из но не ни` плюс трёхбуквенные `для без под над при про через`. Частицы, перед которыми ставится U+00A0: `ли ль же ж бы б`.
- Сочетания по умолчанию: «Сменить раскладку» — двойной левый Shift, «Причесать типографику» — двойной правый Shift. Окно двойного нажатия 0,3 с, как сейчас.
- Исключения типографики по умолчанию: `com.apple.Terminal, com.googlecode.iterm2, com.microsoft.VSCode, com.apple.dt.Xcode, com.todesktop.230313mzl4w4u92` (Cursor) и bundle ID с префиксом `com.jetbrains.`.
- Новые ключи UserDefaults: `typographyLiveEnabled: Bool` (true), `typographyHotkeyEnabled: Bool` (true), `typographyExcludedBundleIDs: [String]`, `hotkeyConvert` и `hotkeyTypography` (JSON `Hotkey`). Набранный текст по-прежнему нигде не хранится и не логируется.
- Коммиты без `Co-Authored-By`.

## Review Focus

1. **Типографика и свитчер на одной клавише.** Свитчер исправил слово на пробеле, а типографика хочет тот же пробел сделать неразрывным. Ожидается: сначала свитчер, а типографика на этом нажатии молчит (контекст сбрасывается). Тест `engineConsumedResetsTypography` в Task 4 (ручной), правило проверено в Task 2 через `reset()`.
2. **Замена меняет длину текста** (`...` → `…`). Память свитчера иначе будет стирать не то число символов. Ожидается: после такой правки память свитчера сбрасывается. Ручная проверка в Task 4: `привет...` + двойной левый Shift не портит текст.
3. **Кавычки, когда контекст потерян после клика.** Ожидается: открывающая или закрывающая выбирается по символу слева, а вложенность считается с последнего сброса. Тест `quoteAfterResetOpens` в Task 2.
4. **Английский текст.** Никаких ёлочек и неразрывных пробелов после «a», «I». Тест `englishTextUntouchedByRussianRules` в Task 1.
5. **Код и команды в обычных приложениях**, например `--help` или `"path"` в Заметках. Ожидается: `--` в начале слова без пробела перед ним не превращается в тире. Тест `doubleHyphenOptionKept` в Task 1.

---

## Структура файлов

```
Sources/ValloCore/Typographer.swift          ← format(_:final:) и правила
Sources/ValloCore/LiveTypographer.swift      ← правка на лету + отмена Backspace
Sources/ValloCore/Hotkey.swift               ← Hotkey, ModifierKey, DoubleTapDetector (заменяет DoubleShiftDetector)
Sources/ValloSwitcher/HotkeyRecorder.swift   ← запись «своего варианта»
Tests/ValloCoreTests/TypographerTests.swift, LiveTypographerTests.swift, HotkeyTests.swift
Изменяются: KeyboardTap.swift, SettingsStore.swift, ContextGuard.swift, SettingsView.swift, EventPoster.swift (без изменений API)
Удаляется: DoubleShiftDetector.swift (+ его тесты переезжают в HotkeyTests)
```

---

### Task 1: Правила типографики

**Files:** Create `Sources/ValloCore/Typographer.swift`; Test `Tests/ValloCoreTests/TypographerTests.swift`

**Interfaces — Produces:**
```swift
public enum Typographer {
    /// final: true — текст закончен (выделение); false — набор продолжается, правила с правым контекстом ждут.
    public static func format(_ text: String, final: Bool = true) -> String
}
```
Правила идемпотентны: `format(format(x)) == format(x)`. Язык кавычек определяется по ближайшим буквам слева, а если слева букв нет, то справа: кириллица → RU, иначе EN. Вложенность кавычек: открытые « без закрывающей до этого места → внутри ставятся „“ (в EN “ → ‘’).

- [ ] **Step 1: Тесты** (`let nb = "\u{00A0}"`), все с `final: true`, если не сказано иное:
  - `dash`: `"Москва - столица"` → `"Москва\(nb)— столица"`; `"раз -- два"` → `"раз\(nb)— два"`; `"- Привет"` → `"— Привет"`; `"кто-то"` не меняется.
  - `minusAndRanges`: `"5 - 3"` → `"5 − 3"`; `"было -5"` → `"было −5"`; `"1990-2000"` → `"1990–2000"`; `"2026-10-30"` и `"+7-999-123-45-67"` не меняются.
  - `quotesRussian`: `"\"Он сказал \"привет\"\""` → `"«Он сказал „привет“»"`.
  - `quotesEnglish`: `"\"Hello\" it's"` → `"“Hello” it’s"`.
  - `nbspPrepositions`: `"в доме и для нас"` → `"в\(nb)доме и\(nb)для\(nb)нас"`; `"пришёл ли он"` → `"пришёл\(nb)ли он"`.
  - `nbspNumbersInitials`: `"5 кг"` → `"5\(nb)кг"`; `"А. С. Пушкин"` → `"А.\(nb)С.\(nb)Пушкин"`; `"№ 5"` → `"№\(nb)5"`.
  - `ellipsisAndSymbols`: `"ну..."` → `"ну…"`; `"(c) 2026"` → `"© 2026"`; `"+-5"` → `"±5"`; `"a -> b"` → `"a → b"`; `"1920x1080"` → `"1920×1080"`; `"1/2"` → `"½"`; `"(tm)"` → `"™"`; `"(r)"` → `"®"`.
  - `spacesCleanup`: `"привет  ,  как"` → `"привет, как"`.
  - `englishTextUntouchedByRussianRules`: `"I am a cat"` не меняется.
  - `doubleHyphenOptionKept`: `"ls --help"` не меняется.
  - `idempotent`: для каждого входа выше `format(format(x)) == format(x)`.
  - `liveWaitsForRightContext`: `format("5 - ", final: false) == "5 - "` (минус или тире, пока непонятно); `format("5 - 3", final: false) == "5 − 3"`; `format("5 - а", final: false) == "5\(nb)— а"`; `format("10-20", final: false) == "10-20"`; `format("10-20 ", final: false) == "10–20 "`.
- [ ] **Step 2:** `swift test --filter TypographerTests`. Ожидается FAIL: `Typographer` не определён.
- [ ] **Step 3:** Реализовать: правила как упорядоченный список регулярных замен (`NSRegularExpression`). Порядок: чистка пробелов → символы → цифры → тире → кавычки → неразрывные пробелы. Кавычки — проход по символам со счётчиком вложенности. Правила с правым контекстом (`(?=…)`) при `final: true` дополнительно принимают конец текста. «Пробел» в правилах — это и обычный, и неразрывный (`[ \u00A0]`), чтобы правила не мешали друг другу.
- [ ] **Step 4:** `swift test`. Ожидается PASS (все старые тесты тоже).
- [ ] **Step 5:** Коммит `feat: typography rules`.

### Task 2: Типографика на лету и отмена

**Files:** Create `Sources/ValloCore/LiveTypographer.swift`; Test `Tests/ValloCoreTests/LiveTypographerTests.swift`

**Interfaces — Consumes:** `Typographer.format`. **Produces:**
```swift
public struct TypoEdit: Equatable, Sendable {
    public let delete: Int        // сколько символов стереть (уже на экране)
    public let insert: String     // что вставить (уже включая набранный символ)
}
public struct LiveTypographer: Sendable {
    public static let contextLimit = 60
    public init()
    /// Человек набрал символ. nil — пропустить символ как есть.
    public mutating func typed(_ char: Character) -> TypoEdit?
    /// Backspace. Сразу после правки — правка-отмена (вернуть набранное), иначе nil (обычный Backspace).
    public mutating func backspace() -> TypoEdit?
    public mutating func reset()
    /// Длина текста на экране изменилась (… вместо ...) — свитчеру надо сбросить память.
    public private(set) var lastEditChangedLength: Bool
}
```
Алгоритм: `raw = context + char`, `formatted = Typographer.format(raw, final: false)`. Если равны — `context = raw`, nil. Иначе общий префикс `p`: `TypoEdit(delete: context.count - p, insert: String(formatted.dropFirst(p)))`. Запомнить отмену: `(shown: formatted.dropFirst(p), typed: raw.dropFirst(p))`, затем `context = formatted`. Контекст обрезается слева до `contextLimit`.

- [ ] **Step 1: Тесты** (помощник `typeAll("…")` возвращает правки по каждому символу):
  - `dashOnSpace`: набрать `"раз - "`. На последнем пробеле `TypoEdit(delete: 2, insert: "\u{00A0}— ")`.
  - `quoteOpensAndCloses`: `"\"да\""`. Первый символ → `TypoEdit(delete: 0, insert: "«")`, последний → `TypoEdit(delete: 0, insert: "»")`.
  - `nbspAfterPreposition`: `"в "` → на пробеле `TypoEdit(delete: 0, insert: "\u{00A0}")`.
  - `backspaceUndoes`: после `"раз - "` вызвать `backspace()` → `TypoEdit(delete: 3, insert: " - ")`, повторный `backspace()` → nil.
  - `backspaceAfterOtherCharDoesNothing`: после `"в "` и `"д"` вызвать `backspace()` → nil.
  - `quoteAfterResetOpens`: `reset()`, затем `"` → `«`.
  - `ellipsisChangesLength`: `"ну..."` → на третьей точке `TypoEdit(delete: 2, insert: "…")` и `lastEditChangedLength == true`.
- [ ] **Step 2:** Запустить. Ожидается FAIL.
- [ ] **Step 3:** Реализовать по алгоритму выше.
- [ ] **Step 4:** `swift test`. Ожидается PASS.
- [ ] **Step 5:** Коммит `feat: live typography with backspace undo`.

### Task 3: Сочетания клавиш

**Files:** Create `Sources/ValloCore/Hotkey.swift`, `Tests/ValloCoreTests/HotkeyTests.swift`; Delete `Sources/ValloCore/DoubleShiftDetector.swift`, `Tests/ValloCoreTests/DoubleShiftDetectorTests.swift`

**Interfaces — Produces:**
```swift
public enum ModifierKey: String, Codable, CaseIterable, Sendable {
    case leftShift, rightShift, leftOption, rightOption, leftControl
    public var keyCode: UInt16      // 56, 60, 58, 61, 59
    public var title: String        // «Двойной левый Shift» …
}
public enum Hotkey: Codable, Equatable, Sendable {
    case doubleTap(ModifierKey)
    case combo(keyCode: UInt16, command: Bool, option: Bool, control: Bool, shift: Bool)
    public static let defaultConvert: Hotkey = .doubleTap(.leftShift)
    public static let defaultTypography: Hotkey = .doubleTap(.rightShift)
    public var title: String        // для combo: «⌃⌥T» и т. п.
}
public struct DoubleTapDetector: Sendable {
    public static let window: TimeInterval = 0.3
    public init(key: ModifierKey)
    public var key: ModifierKey
    public mutating func keyDown(at: TimeInterval)
    public mutating func keyUp(at: TimeInterval) -> Bool
    public mutating func otherInput()
}
```
- [ ] **Step 1: Тесты:** перенести 5 тестов из `DoubleShiftDetectorTests` на `DoubleTapDetector(key: .leftShift)`; `hotkeyCodableRoundTrip` для обоих видов `Hotkey`; `defaultsDiffer` (`defaultConvert != defaultTypography`); `comboTitle`: `.combo(keyCode: 17, command: false, option: true, control: true, shift: false).title == "⌃⌥T"`.
- [ ] **Step 2:** Запустить. Ожидается FAIL.
- [ ] **Step 3:** Реализовать (детектор — код `DoubleShiftDetector` с параметром клавиши). Удалить старые файлы.
- [ ] **Step 4:** `swift test`. Ожидается PASS.
- [ ] **Step 5:** Коммит `feat: configurable hotkeys model`.

### Task 4: Подключение в приложении

**Files:** Modify `KeyboardTap.swift`, `SettingsStore.swift`, `ContextGuard.swift`, `AppDelegate.swift`

**Interfaces — Consumes:** Tasks 1–3. **Produces:** `SettingsStore`: `typographyLiveEnabled`, `typographyHotkeyEnabled`, `typographyExcludedBundleIDs`, `hotkeyConvert: Hotkey`, `hotkeyTypography: Hotkey` (значения по умолчанию из Global Constraints); `ContextGuard.typographyAllowed(store:) -> Bool` (исключения типографики, префикс `com.jetbrains.`, Secure Input, поля пароля/e-mail/телефона/URL); `KeyboardTap.reloadHotkeys()`.

Порядок на keyDown: (1) Backspace: если `live.backspace()` дал правку — проглотить, выполнить, `engine.reset()`; (2) `engine.handleKey` как сейчас; если движок проглотил → `live.reset()`; (3) иначе, если типографика на лету разрешена и символ есть (`layouts.pair.layout(actual).character(for:)`), то `live.typed`. Если есть правка — проглотить, выполнить `[.delete(n), .insert(s)]` через proxy; если `lastEditChangedLength` — `engine.reset()`. Клик, смена приложения, Cmd/Ctrl/Option → `live.reset()`.

Сочетания: два `DoubleTapDetector` (по одному на действие, если сочетание — двойное нажатие); `combo` сравнивается на keyDown по keyCode и флагам и проглатывается. Типографика по сочетанию (не при Secure Input): `SelectionReader.selectedText` → `Typographer.format` → если текст изменился, `EventPoster.replaceSelection`.

- [ ] **Step 1:** Реализовать. `swift build -c release` без ошибок, `swift test` PASS.
- [ ] **Step 2: Ручная проверка с учеником** в TextEdit: `Москва - столица`, `"привет"`, `в доме`, `ну...`, `5 - 3`; Backspace сразу после тире возвращает дефис; в Терминале ничего не меняется; выделить `"текст" - тест` и нажать двойной правый Shift → `«текст» — тест`; **Review Focus 1:** `ghbdtn` + пробел (свитчер исправил, неразрывного пробела нет); **Review Focus 2:** `привет...` + двойной левый Shift — текст не портится.
- [ ] **Step 3:** Коммит `feat: typography in the app`.

### Task 5: Настройки

**Files:** Create `Sources/ValloSwitcher/HotkeyRecorder.swift`; Modify `SettingsView.swift`

**Interfaces — Consumes:** Task 4. **Produces:** секции в окне настроек:
- **«Типографика»:** «На лету», «По сочетанию», список исключений (как у свитчера: добавить, убрать).
- **«Сочетания»:** по строке на действие с выпадающим списком (`ModifierKey.allCases` + «Свой вариант…»). «Свой вариант…» открывает `HotkeyRecorder`: окно «Нажми сочетание», ловит первый keyDown с модификатором через `NSEvent.addLocalMonitorForEvents`, Esc — отмена. Конфликт (то же, что у другого действия) → предупреждение «Это сочетание уже занято: …», не сохраняется. Кнопка «По умолчанию». После изменения вызывается `KeyboardTap.reloadHotkeys()`.

- [ ] **Step 1:** Реализовать. Сборка без ошибок.
- [ ] **Step 2: Ручная проверка:** выбрать двойной Option для смены раскладки — работает, а двойной левый Shift больше не работает; «Свой вариант…» → ⌃⌥T для типографики — работает; попытка назначить то же, что у другого действия, даёт предупреждение; «По умолчанию» возвращает оба Shift; отключить «На лету» — тире больше не ставится, двойной правый Shift работает.
- [ ] **Step 3:** Коммит `feat: typography and hotkey settings`.

### Task 6: Финал

- [ ] **Step 1:** `swift test`. Ожидается всё PASS.
- [ ] **Step 2:** Финальное ревью свежим проверяющим, исправление Critical/Important с тестами.
- [ ] **Step 3:** Обновить README и `Support/INSTALL.txt` (типографика, сочетания). Затем `scripts/install.sh`, `scripts/package.sh`, коммит, пуш.
