# Vallo Switcher v1 — план работ

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Менюбар-приложение для macOS, которое исправляет текст, набранный не в той раскладке (RU ↔ EN): автоматически во время набора и по двойному левому Shift.

**Architecture:** Swift Package из трёх целей. `ValloCore` — чистая логика без системных API (раскладки как таблицы, конвертер, словари, судья, движок, детектор двойного Shift); покрывается автотестами. `ValloSwitcher` — тонкая AppKit-оболочка: перехват клавиатуры (CGEventTap), реальные раскладки (UCKeyTranslate / TIS), отправка событий, буфер обмена, Accessibility, меню и настройки. `dump-layouts` — утилита, которая снимает реальные раскладки системы в JSON-фикстуры для тестов. `.app` собирается скриптом и подписывается стабильной подписью.

**Tech Stack:** Swift 6 (тулчейн Xcode), SwiftPM, Swift Testing, AppKit + SwiftUI (окно настроек), Carbon TIS / UCKeyTranslate, CoreGraphics CGEventTap, ApplicationServices AX, ServiceManagement (SMAppService).

**Spec:** `docs/superpowers/specs/2026-10-29-vallo-switcher-design.md`

## Global Constraints

- Только macOS, минимум **macOS 14**. Приложение живёт в строке меню, без Dock: `LSUIElement = true`.
- Bundle ID `com.vallo.switcher`, имя `Vallo Switcher`.
- Работает **полностью офлайн**. Никаких сетевых запросов в коде приложения.
- Набранный текст **нигде не сохраняется**, только буфер в памяти (не больше 200 нажатий). В UserDefaults лежат только: `autoSwitchEnabled: Bool` (по умолчанию `true`), `excludedBundleIDs: [String]` (по умолчанию `["com.apple.Terminal", "com.googlecode.iterm2", "com.microsoft.VSCode"]`), `learnedWords: [String]`, `debugMode: Bool` (по умолчанию `false`).
- Языки только RU и EN. Раскладки берутся **из системы** (у автора «Русская» маковская + ABC/U.S., клавиатура ANSI), с учётом типа клавиатуры (ANSI/ISO/JIS).
- Двойной Shift: только **левый** Shift (keyCode 56), два чистых нажатия, второе нажатие начинается не позже **0,3 с** после первого отпускания.
- Автопереключение: решение не раньше **3-й буквы**; слова из 1–2 букв автоматически не трогаются; переключение назад — максимум один раз за слово.
- Нормализация словарей и проверок: `lowercased()`, `ё → е`.
- Свои синтетические события помечаются `eventSourceUserData = 0x56414C4C` («VALL»), и тап их игнорирует.
- Коммиты без строки `Co-Authored-By` (правило автора).
- **Уточнение спеки (сценарий Б).** Автоисправление теперь бывает посреди слова, поэтому откат действует, пока ты дописываешь это слово, и сразу после его конца. Откат сбрасывается, когда начинается следующее слово, на клик, смену окна или раскладки.

## Review Focus

1. **Быстрый набор во время замены.** Ты продолжаешь печатать, пока свитчер стирает и вставляет. Ни одна буква не должна потеряться или переставиться. Проверяется вручную в Task 7.
2. **Автозамена самих приложений** (Заметки, Telegram) меняет текст за спиной свитчера, и число Backspace становится неверным. Ожидается: свитчер не превращает текст в кашу. Защита: буфер сбрасывается на любое нажатие с Cmd/Ctrl/Option и на клик. Ручная проверка в Task 10, тест `commandComboResetsBuffer` в Task 5.
3. **Caps Lock включён.** `GHBDTN` → «ПРИВЕТ», и регистр не ломается. Тест `capsLockRendersUppercase` в Task 2.
4. **Двойной Shift, когда ничего не набрано и не выделено.** Ничего не происходит, раскладка тоже не меняется. Тест `doubleShiftWithNothingDoesNothing` в Task 5.
5. **Выделение в Figma, Telegram и других Electron-приложениях.** Accessibility там часто не отдаёт выделенный текст, поэтому нужен запасной путь через Cmd+C, а буфер обмена потом возвращается как был. Ручная проверка в Task 8.

---

## Структура файлов

```
Package.swift
Data/layouts/{ansi,iso}-{en,ru}.json     ← фикстуры раскладок (генерирует dump-layouts)
Data/wordlists/{en,ru}.txt               ← словари частотности, «слово количество» на строку
Sources/ValloCore/KeyStroke.swift        ← KeyStroke, Language, KeyLayout, TableKeyLayout, LayoutPair
Sources/ValloCore/Converter.swift
Sources/ValloCore/Lexicon.swift
Sources/ValloCore/Judge.swift            ← Judge, Verdict, SpecialText
Sources/ValloCore/Engine.swift           ← Engine, KeyEvent, Action, EngineOutput
Sources/ValloCore/DoubleShiftDetector.swift
Sources/ValloSwitcher/main.swift, AppDelegate.swift, StatusMenu.swift
Sources/ValloSwitcher/Permissions.swift, KeyboardTap.swift, SystemLayouts.swift
Sources/ValloSwitcher/EventPoster.swift, SelectionReader.swift, ContextGuard.swift
Sources/ValloSwitcher/SettingsStore.swift, SettingsView.swift
Sources/dump-layouts/main.swift
Support/Info.plist
scripts/build-app.sh
Tests/ValloCoreTests/*.swift
```

---

### Task 0: Подготовка Мака (делает автор, ассистент диктует)

- [ ] **Step 1:** Автор в Терминале переключает инструменты на Xcode и принимает лицензию:
  `sudo xcode-select -s /Applications/Xcode.app/Contents/Developer && sudo xcodebuild -license accept`
- [ ] **Step 2:** Автор открывает Xcode → Settings → Accounts → «+» → Apple ID. Входит своим Apple ID и создаёт сертификат (Manage Certificates → «+» → Apple Development).
- [ ] **Step 3: Проверка.** Ассистент запускает `swift --version` (должна быть версия Swift без ошибки про лицензию) и `security find-identity -v -p codesigning` (ожидается строка `Apple Development: …`).
- [ ] **Step 4:** `git init` в `plug`, первый коммит со спекой и планом. Как именно делаем git, решаем с автором перед этим шагом.

### Task 1: Каркас пакета, модель нажатий, фикстуры раскладок

**Files:**
- Create: `Package.swift`, `Sources/ValloCore/KeyStroke.swift`, `Sources/dump-layouts/main.swift`, `Data/layouts/*.json`
- Test: `Tests/ValloCoreTests/LayoutFixtureTests.swift`, `Tests/ValloCoreTests/TestSupport.swift`

**Interfaces — Produces:**
```swift
public struct KeyStroke: Hashable, Codable, Sendable { public let keyCode: UInt16; public let shift: Bool; public let capsLock: Bool }
public enum Language: String, Codable, Sendable { case en, ru; public var other: Language }
public protocol KeyLayout: Sendable { var language: Language { get }; func character(for: KeyStroke) -> String? }
public struct TableKeyLayout: KeyLayout, Codable {          // JSON: {"language":"ru","entries":[{"keyCode":5,"shift":false,"capsLock":false,"char":"п"},...]}
    public init(language: Language, table: [KeyStroke: String])
    public static func load(url: URL) throws -> TableKeyLayout
}
public struct LayoutPair: Sendable { public init(en: any KeyLayout, ru: any KeyLayout); public func layout(_: Language) -> any KeyLayout }
// TestSupport: func repoURL(_ relative: String) -> URL   (от #filePath вверх до корня репо)
//              func fixturePair(_ kind: String) -> LayoutPair   // "ansi" | "iso"
```

- [ ] **Step 1:** `Package.swift`: platforms `.macOS(.v14)`. Цели: library `ValloCore`, executable `ValloSwitcher` (зависит от ValloCore), executable `dump-layouts` (зависит от ValloCore), testTarget `ValloCoreTests`.
- [ ] **Step 2:** Реализовать типы из Interfaces в `KeyStroke.swift`.
- [ ] **Step 3:** `dump-layouts`: находит включённые в системе раскладки EN и RU (`TISCreateInputSourceList`, тип `kTISTypeKeyboardLayout`, язык по `kTISPropertyInputSourceLanguages`). Для ANSI и ISO подбирает `kbdType`: перебирает 0…255, берёт первый, для которого `KBGetLayoutType` возвращает `kKeyboardANSI` / `kKeyboardISO`. Для keyCode 0…127 × shift × capsLock вызывает `UCKeyTranslate` и пишет 4 файла в `Data/layouts/`. Пропускает пустые символы и управляющие (< U+0020).
- [ ] **Step 4:** Запустить `swift run dump-layouts`. Ожидается: 4 файла, в каждом ≥ 90 записей.
- [ ] **Step 5: Тест** `fixturesRenderPrivet` (для `ansi` и `iso`): нажатия, которые в EN дают `g h b d t n`, в RU дают «п р и в е т». `fixturesContainYo`: в RU-раскладке обоих типов есть «ё» и «Ё»; ANSI- и ISO-нажатие для «ё» может отличаться, тест это фиксирует (печатает keyCode). `bracketsAreLetters`: в RU нажатия EN `[ ] ; ' , .` дают `х ъ ж э б ю`.
- [ ] **Step 6:** `swift test`. Ожидается: PASS.
- [ ] **Step 7:** Коммит `feat: package skeleton and layout fixtures`.

### Task 2: Конвертер

**Files:** Create `Sources/ValloCore/Converter.swift`; Test `Tests/ValloCoreTests/ConverterTests.swift`

**Interfaces — Consumes:** Task 1. **Produces:**
```swift
public enum Converter {
    public static func render(_ strokes: [KeyStroke], in layout: any KeyLayout) -> String
    public static func strokes(for text: String, in layout: any KeyLayout) -> [KeyStroke?]   // nil — символа нет в раскладке
    public static func convert(_ text: String, from: any KeyLayout, to: any KeyLayout) -> String   // неотображаемое остаётся как есть
}
```
Обратная таблица символ → нажатие строится один раз на раскладку. Если для символа есть несколько нажатий, предпочитать нажатие без capsLock.

- [ ] **Step 1: Тесты** (на `ansi` и `iso`): `convert("ghbdtn", en→ru) == "привет"`; `convert(",eltn", en→ru) == "будет"`; `convert("Ghbdtn", en→ru) == "Привет"`; `convert("GHBDTN", en→ru) == "ПРИВЕТ"`; `convert("руддщ", ru→en) == "hello"`; `convert("ghbdtn rfr ltkf", en→ru) == "привет как дела"`; round-trip «ёжик» ru→en→ru == «ёжик»; `convert("№", en→ru)`, где символа нет в EN-раскладке, == «№»; `capsLockRendersUppercase`: `render` нажатий `ghbdtn` с `capsLock: true` в RU == «ПРИВЕТ».
- [ ] **Step 2:** `swift test --filter ConverterTests`. Ожидается: FAIL, функции не определены.
- [ ] **Step 3:** Реализовать `Converter`.
- [ ] **Step 4:** `swift test`. Ожидается: PASS.
- [ ] **Step 5:** Коммит `feat: layout converter`.

### Task 3: Словари

**Files:** Create `Sources/ValloCore/Lexicon.swift`, `Data/wordlists/{en,ru}.txt`; Test `Tests/ValloCoreTests/LexiconTests.swift`

**Interfaces — Produces:**
```swift
public struct Lexicon: Sendable {
    public init(words: [String])                              // нормализует: lowercased, ё→е
    public static func load(url: URL, limit: Int = 50_000) throws -> Lexicon   // формат строки: "слово количество"
    public func isWord(_ s: String) -> Bool
    public func isPrefix(_ s: String) -> Bool                 // любой префикс любого слова, включая само слово
    public func trigramScore(_ s: String) -> Double           // средний log P триграмм с границами "^^s$", сглаживание add-one
    public static func normalize(_ s: String) -> String
}
```

- [ ] **Step 1:** Данные — частотные списки hermitdave/FrequencyWords (OpenSubtitles 2018, `en_50k.txt`, `ru_50k.txt`). **Перед скачиванием спросить автора** (файл, источник, размер ~1 МБ каждый), потом скачать в `Data/wordlists/en.txt` и `ru.txt`. В `Data/wordlists/README.md` указать источник и лицензию (CC-BY-SA-4.0).
- [ ] **Step 2: Тесты** (маленький лексикон `["привет","при","hello"]`): `isPrefix("пр")`, `isPrefix("при")`, `!isPrefix("прф")`, `isWord("при")`, `!isWord("прив")`; `isWord("ёж")` для лексикона `["еж"]`. На реальных списках: `ru.isWord("привет")`, `ru.isWord("будет")`, `en.isWord("hello")`, `!en.isPrefix("ghb")`, `ru.isPrefix("при")`, `ru.trigramScore("привет") > ru.trigramScore("ghbdtn")`.
- [ ] **Step 3:** Запустить тесты. Ожидается: FAIL.
- [ ] **Step 4:** Реализовать: `Set<String>` слов, `Set<String>` префиксов, `[String: Int]` триграмм.
- [ ] **Step 5:** `swift test`. Ожидается: PASS. Плюс `measureLoad`: загрузка двух списков < 1 с в release.
- [ ] **Step 6:** Коммит `feat: lexicon with prefixes and trigrams`.

### Task 4: Судья

**Files:** Create `Sources/ValloCore/Judge.swift`; Test `Tests/ValloCoreTests/JudgeTests.swift`

**Interfaces — Consumes:** `Lexicon`, `Language`. **Produces:**
```swift
public enum Verdict: Equatable, Sendable { case wait, keep, switchLanguage(reason: String) }
public struct Judge: Sendable {
    public static let trigramMargin = 2.0
    public init(en: Lexicon, ru: Lexicon, learned: Set<String>)
    public var learned: Set<String>                                    // хранится нормализованным: строка в языке, в котором её набрали
    public func midWord(current: String, alternative: String, currentLanguage: Language) -> Verdict
    public func wordEnd(current: String, alternative: String, currentLanguage: Language) -> Verdict
    public func shouldRevert(original: String, converted: String, originalLanguage: Language) -> Bool
    public static func core(_ s: String) -> String                     // обрезает небуквенные символы в начале и в конце
}
public enum SpecialText { public static func isSpecial(_ raw: String) -> Bool }
```
Правила (`cur = core(current)`, `alt = core(alternative)`, `L` — текущий лексикон, `A` — другой):
- `midWord`: если `isSpecial(current)` или `cur.count < 3`, то `.wait`. Если есть выученное слово, которое начинается с `normalize(cur)`, то `.wait`. Если `!L.isPrefix(cur) && A.isPrefix(alt)`, то `.switchLanguage`. Иначе `.wait`.
- `wordEnd`: если special, длина < 3 или слово выучено, то `.keep`. Если `L.isWord(cur)`, то `.keep`. Если `A.isWord(alt)`, то `.switchLanguage`. Если `A.trigramScore(alt) - L.trigramScore(cur) > trigramMargin`, то `.switchLanguage`. Иначе `.keep`.
- `shouldRevert`: `!A.isPrefix(core(converted)) && L.trigramScore(core(original)) > A.trigramScore(core(converted))`, где A — язык converted. Проверять «оригинал стал префиксом L» нельзя: раз в момент переключения начало слова не было префиксом L, более длинное слово тоже им не станет.
- `isSpecial`: содержит цифру, `@`, `://` или `www.`; либо заканчивается на `.` + одно из `com ru org net io dev app me co ua by kz info`.
- `reason` — человекочитаемая строка для режима отладки, например `"«ghb» не начало EN-слова, «при» — начало RU"`.

- [ ] **Step 1: Тесты** (маленькие лексиконы EN `["hello","help","the"]`, RU `["привет","при","будет"]`): `midWord("gh","пр",.en) == .wait`; `midWord("ghb","при",.en)` — это switch; `midWord("hel","рук",.en) == .wait`; `midWord(",el","буд",.en) == .wait` (запятая в EN отрезается, остаётся 2 буквы); `midWord(",elt","буде",.en)` — это switch; `wordEnd("ghbdtn","привет",.en)` — это switch; `wordEnd("hello","руддщ",.en) == .keep`; `wordEnd("z","я",.en) == .keep`; `midWord("ghb","при",.en)` с `learned = ["ghbdtn"]` == `.wait`; `isSpecial("me@mail")`, `isSpecial("site.com")`, `isSpecial("+79991234567")`, `!isSpecial("k.lb")` (это «люди»); `revertsWhenConvertedStopsBeingPrefix`: при EN `["ghost","abz"]`, RU `["при"]` вызов `shouldRevert(original:"ghbz", converted:"прия", originalLanguage:.en) == true`; при RU `["привет"]` вызов `shouldRevert(original:"ghbd", converted:"прив", originalLanguage:.en) == false`.
- [ ] **Step 2:** Запустить тесты. Ожидается: FAIL.
- [ ] **Step 3:** Реализовать `Judge` и `SpecialText`.
- [ ] **Step 4:** `swift test`. Ожидается: PASS.
- [ ] **Step 5:** Коммит `feat: judge`.

### Task 5: Движок (буфер + сценарии А–В)

**Files:** Create `Sources/ValloCore/Engine.swift`; Test `Tests/ValloCoreTests/EngineTests.swift`

**Interfaces — Consumes:** Tasks 1–4. **Produces:**
```swift
public struct KeyEvent: Sendable { public let stroke: KeyStroke; public let hasCommandControlOrOption: Bool }
public enum Action: Equatable, Sendable { case delete(Int), insert(String), switchLayout(Language), replaceSelection(String), learn(String) }
public enum EngineOutput: Equatable, Sendable { case passThrough, consume([Action]) }
public final class Engine {
    public init(layouts: LayoutPair, judge: Judge, currentLanguage: Language)
    public var autoSwitchEnabled: Bool          // false → handleKey никогда не делает автопереключение
    public var judge: Judge                     // для обновления learned снаружи
    public private(set) var currentLanguage: Language
    public private(set) var lastDecision: String?
    public func handleKey(_ e: KeyEvent) -> EngineOutput
    public func handleDoubleShift(selection: String?) -> [Action]
    public func reset()                          // клик, смена приложения
    public func externalLayoutChange(to: Language)   // сброс + обновление currentLanguage
}
```
Поведение:
- Буфер: `[(stroke, language)]`, не больше 200. Отображаемый текст: каждое нажатие отрисовывается в своём языке.
- Сброс (`reset`, без действий): `hasCommandControlOrOption`, Return (36), Tab (48), Esc (53), стрелки 123–126, Home/End/PgUp/PgDn (115, 119, 116, 121), Forward Delete (117). Backspace (51) — `.passThrough` и удаление последнего нажатия, в том числе внутри текущего слова.
- **Граница слова** — нажатие, которое ни в EN, ни в RU не даёт букву (`Character.isLetter`). Остальные нажатия — часть слова.
- Буква в слове, где `autoSwitchEnabled`, слово не заблокировано и `judge.midWord` == switch: возвращает `.consume([.delete(n), .insert(слово с этой буквой в другом языке), .switchLayout(другой)])`, где n — отображаемая длина слова без этой буквы. Слово помечается «автоисправлено», запоминается отменяемое действие.
- Буква в автоисправленном слове, где `judge.shouldRevert` и возврата ещё не было: обратная замена всего слова, возврат раскладки, слово блокируется, отменяемое действие стирается.
- Граница, когда слово не автоисправлялось и `judge.wordEnd` == switch: `.consume([.delete(n), .insert(слово + граница в другом языке), .switchLayout])`.
- Отменяемое действие живёт, пока набирается это слово и его граница. Первая буква следующего слова или сброс его стирают.
- `handleDoubleShift`:
  1. Если есть отменяемое действие, то `[.delete(слово+граница), .insert(слово+граница в исходном языке), .switchLayout(исходный), .learn(normalize(core(слово в исходном)))]`, и слово блокируется.
  2. Иначе, если `selection` не пустое: исходный язык — тот, чьих букв в выделении больше (кириллица → ru). Возвращается `[.replaceSelection(convert), .switchLayout(другой)]`, буфер сбрасывается.
  3. Иначе, если буфер не пуст: всё переводится в `currentLanguage.other`, `[.delete(отображаемая длина), .insert(...), .switchLayout(other)]`.
  4. Иначе `[]`.

- [ ] **Step 1: Тесты** (фикстуры `ansi` + маленький Judge из Task 4; помощник `type("ghbdtn ", engine)` отдаёт нажатия по EN-фикстуре):
  - `autoSwitchMidWord`: «g», «h» — passThrough; «b» — `.consume([.delete(2), .insert("при"), .switchLayout(.ru)])`; дальнейшие буквы — passThrough.
  - `undoRightAfterAutoSwitch`: набрать `ghbdtn` и пробел, двойной Shift → `[.delete(7), .insert("ghbdtn "), .switchLayout(.en), .learn("ghbdtn")]`; после этого в новом слове `ghb` автопереключения нет.
  - `undoExpiresOnNextWord`: после `ghbdtn ` и буквы `x` двойной Shift идёт по сценарию В, а не откатывает.
  - `doubleShiftConvertsWholeRun`: при выключенном авто набрать `ghbdtn rfr ltkf` → `[.delete(15), .insert("привет как дела"), .switchLayout(.ru)]`.
  - `doubleShiftSelection`: `selection: "руддщ"` → `[.replaceSelection("hello"), .switchLayout(.en)]`.
  - `doubleShiftWithNothingDoesNothing`: пустой буфер, нет выделения → `[]`.
  - `commandComboResetsBuffer`: набрать `ghbd`, потом Cmd+Z, двойной Shift → `[]`.
  - `commaInsideWordIsLetter`: при выключенном авто набрать `,eltn`, двойной Shift → insert «будет».
  - `backspaceShrinksBuffer`: набрать `ghbdtnn`, Backspace, двойной Shift → insert «привет».
  - `shortWordsNotAuto`: `z ` — passThrough на всех нажатиях.
  - `autoSwitchDisabled`: при `autoSwitchEnabled = false` набрать `ghbdtn ` — всё passThrough.
- [ ] **Step 2:** Запустить тесты. Ожидается: FAIL.
- [ ] **Step 3:** Реализовать `Engine`.
- [ ] **Step 4:** `swift test`. Ожидается: PASS, включая тесты прошлых задач.
- [ ] **Step 5:** Коммит `feat: engine scenarios A-V`.

### Task 6: Детектор двойного Shift

**Files:** Create `Sources/ValloCore/DoubleShiftDetector.swift`; Test `Tests/ValloCoreTests/DoubleShiftDetectorTests.swift`

**Interfaces — Produces:**
```swift
public struct DoubleShiftDetector: Sendable {
    public static let window: TimeInterval = 0.3
    public init()
    public mutating func leftShiftDown(at t: TimeInterval)
    public mutating func leftShiftUp(at t: TimeInterval) -> Bool      // true — сработал двойной Shift
    public mutating func otherInput()                                // любая другая клавиша, правый Shift, клик
}
```
- [ ] **Step 1: Тесты:** нажатия вниз/вверх в моменты 0 / 0,05 / 0,2 / 0,25 → второе отпускание даёт true; второе нажатие через 0,4 с после первого отпускания → false; `otherInput()` между нажатием и отпусканием (Shift+буква) → false; три быстрых Shift подряд → true только один раз.
- [ ] **Step 2:** FAIL → реализовать → `swift test` PASS.
- [ ] **Step 3:** Коммит `feat: double shift detector`.

### Task 7: Приложение: меню, разрешения, перехват и замена текста

**Files:** Create `Support/Info.plist`, `scripts/build-app.sh`, `Sources/ValloSwitcher/{main,AppDelegate,StatusMenu,Permissions,KeyboardTap,SystemLayouts,EventPoster,SettingsStore}.swift`

**Interfaces — Consumes:** `Engine`, `DoubleShiftDetector`, `Lexicon`, `Judge`, `LayoutPair`. **Produces (внутри приложения):**
```swift
final class SystemLayouts { init() throws; var pair: LayoutPair; var current: Language; func select(_: Language); var onExternalChange: (Language) -> Void }
    // SystemKeyLayout: KeyLayout поверх UCKeyTranslate + LMGetKbdType(); пересчёт при kTISNotifySelectedKeyboardInputSourceChanged
    // и при смене типа клавиатуры; select() ставит флаг «это мы», чтобы своё переключение не считалось внешним
enum EventPoster { static let marker: Int64 = 0x56414C4C; static func perform(_: [Action], store: SettingsStore, layouts: SystemLayouts) }
    // delete → Backspace keyCode 51 n раз; insert → CGEvent с keyboardSetUnicodeString кусками по 20 символов
final class KeyboardTap { init(engine: Engine, detector: DoubleShiftDetector, ...); func start() -> Bool }
    // слушает keyDown, flagsChanged, left/rightMouseDown; пропускает события с marker; на kCGEventTapDisabledByTimeout включает тап заново
final class SettingsStore { autoSwitchEnabled, excludedBundleIDs, learnedWords, debugMode }   // UserDefaults, значения по умолчанию из Global Constraints
enum Permissions { static var accessibility: Bool; static var inputMonitoring: Bool; static func openSettings() }
```
Меню (SF Symbol `keyboard`, без разрешений — `exclamationmark.triangle`): «Автопереключение» (галочка), «Настройки…», «Выйти». Окно первого запуска: что дать и зачем, кнопка «Открыть настройки».

- [ ] **Step 1:** `Info.plist`: `CFBundleIdentifier com.vallo.switcher`, `CFBundleName Vallo Switcher`, `LSUIElement true`, `LSMinimumSystemVersion 14.0`. Скрипт `build-app.sh`: `swift build -c release --product ValloSwitcher`, собирает `build/Vallo Switcher.app` (бинарник, Info.plist, `Data/wordlists` → `Contents/Resources/wordlists`), выполняет `codesign --force --options runtime --sign "${SIGN_ID:-Apple Development}"`.
- [ ] **Step 2:** Реализовать файлы из списка. Словари загружаются из `Bundle.main.resourceURL/wordlists`.
- [ ] **Step 3:** `scripts/build-app.sh && open "build/Vallo Switcher.app"`. Ожидается: иконка в меню, окно разрешений.
- [ ] **Step 4: Ручная проверка с автором** в TextEdit (дать разрешения, перезапустить): `ghbdtn` → «привет» с переключением на 3-й букве; откат двойным Shift и повторный набор `ghbdtn` (не трогается); `ghbdtn rfr ltkf` при выключенном авто + Shift → «привет как дела»; `Ghbdtn` → «Привет»; **Review Focus 1:** быстро набрать «ghbdtn vbh» без пауз, ни одна буква не теряется.
- [ ] **Step 5: Пересборка не сбрасывает разрешения.** Пересобрать, запустить и убедиться, что Мак не спрашивает разрешения заново.
- [ ] **Step 6:** Коммит `feat: menubar app with tap and replacement`.

### Task 8: Двойной Shift по выделению

**Files:** Create `Sources/ValloSwitcher/SelectionReader.swift`; Modify `KeyboardTap.swift`, `EventPoster.swift`

**Interfaces — Produces:**
```swift
enum SelectionReader { static func selectedText() -> String? }      // сначала kAXSelectedTextAttribute у фокусного элемента; если не вышло — Cmd+C с marker,
                                                                    // ждать смены pasteboard.changeCount до 300 мс; пусто или нет смены → nil
// EventPoster.replaceSelection: сохранить все элементы и типы NSPasteboard.general, положить текст, Cmd+V, через 300 мс вернуть сохранённое
```
- [ ] **Step 1:** Реализовать. В `KeyboardTap`: на двойной Shift сначала узнать у Engine, есть ли отменяемое действие. Если есть, выделение не читать. Если нет, прочитать выделение и вызвать `handleDoubleShift(selection:)`.
- [ ] **Step 2: Ручная проверка:** в TextEdit, Заметках, Safari и Figma (**Review Focus 5**) выделить `руддщ цщкдв` + двойной Shift → «hello world». Перед этим скопировать картинку и после проверки вставить её: картинка на месте.
- [ ] **Step 3:** Коммит `feat: convert selection`.

### Task 9: Исключения, настройки, отладка

**Files:** Create `Sources/ValloSwitcher/ContextGuard.swift`, `SettingsView.swift`; Modify `KeyboardTap.swift`, `StatusMenu.swift`

**Interfaces — Produces:**
```swift
enum ContextGuard { static func autoSwitchAllowed(store: SettingsStore) -> Bool; static var secureInputActive: Bool }
    // false, если: frontmost bundleID в excludedBundleIDs; IsSecureEventInputEnabled(); у фокусного AX-элемента subrole AXSecureTextField;
    // тип поля email/tel/password, если браузер его сообщает (см. Step 1)
```
Окно настроек (SwiftUI): переключатель автопереключения; список приложений-исключений (добавить через NSOpenPanel по `/Applications`, удалить); список запомненных слов (удалить по одному, «Очистить всё»); «Запускать вместе с Маком» (`SMAppService.mainApp`); «Режим отладки». В режиме отладки в меню видны последние 10 `engine.lastDecision`. Пока активен Secure Input, иконка `appearsDisabled`, опрос раз в 1 с.

- [ ] **Step 1: Разведка.** Временный отладочный вывод всех атрибутов фокусного AX-элемента в полях `type=email`, `type=tel` и `type=password` на тестовой HTML-странице в Safari и Chrome. Если какой-то атрибут сообщает тип поля, использовать его. Если нет, записать в спеку, что такие поля прикрывают только `SpecialText` и список приложений.
- [ ] **Step 2:** Реализовать. Перед `engine.handleKey` проверять `ContextGuard.autoSwitchAllowed`; если нельзя, `engine.autoSwitchEnabled = false` для этого нажатия. Ручной Shift работает всегда, кроме Secure Input. `.learn` сохраняет слово в `SettingsStore` и обновляет `engine.judge.learned`.
- [ ] **Step 3: Ручная проверка:** в Терминале `ghbdtn` не меняется, а Shift работает; в поле пароля ничего не происходит, иконка серая; запомненное слово видно в настройках, после удаления снова исправляется; при включённом «Запуске с Маком» приложение есть в «Объектах входа».
- [ ] **Step 4:** Коммит `feat: exclusions, settings, debug`.

### Task 10: Финальная проверка по чек-листу

- [ ] **Step 1:** `swift test`. Ожидается: всё PASS.
- [ ] **Step 2:** Автор проходит чек-лист спеки: TextEdit, Telegram, браузер, Figma, Заметки; поле пароля; приложение-исключение; откат; выделение с сохранением буфера обмена; внешняя клавиатура, если есть; **Review Focus 2:** набор в Заметках и Telegram с их автозаменой, чтобы не было каши.
- [ ] **Step 3:** Что бесит, записываем отдельными пунктами и правим по одному.
- [ ] **Step 4:** Коммит и тег `v0.1`.
