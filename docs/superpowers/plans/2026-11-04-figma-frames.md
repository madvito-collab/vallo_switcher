# Типографика фреймов в Фигме — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** двойной правый Shift на выделенных фреймах в настольной Фигме прогоняет все текстовые слои внутри через опечатки → «ё» → запятые → типографику свитчера и показывает окошко со статистикой.

**Architecture:** тонкий плагин Фигмы (простой JS, без сборки) держит WebSocket к локальному серверу свитчера. Свитчер решает всё: что нажато, тексты, правила, статистика, правки (диф в UTF-16). Плагин только собирает тексты и применяет правки. Чистая логика (сообщения, диф, статистика) — в `ValloCore` с тестами.

**Tech Stack:** Swift 6 / SwiftPM, Network.framework (`NWListener` + `NWProtocolWebSocket`), Accessibility API, Figma Plugin API (JS).

**Spec:** `docs/superpowers/specs/2026-11-04-figma-frames-design.md`

## Global Constraints

- macOS 14+, настольная Фигма (`com.figma.Desktop`); Фигма в браузере — не цель.
- В Фигме регистр букв и точку в конце не трогаем никогда: `CommaService.fix(..., capitalize: false)` независимо от `store.typoCapitalizeEnabled`.
- Шаги для слоя — ровно как у выделенного текста: `typos.correctPhrase` → `yo.fixPhrase` (если `store.yoEnabled`) → `commas.fix` → `Typographer.format`.
- Только выделенное и всё внутри (включая скрытые, заблокированные, экземпляры и главные компоненты).
- Сервер — только `127.0.0.1`, порт **48731**, принимать только `Origin: null` (или без Origin).
- Тексты интерфейса — на «вы», через `Typographer.format`. В коде/доках — «автор», не «ученик».
- Плагин правил не содержит.
- Журнал отладки — без текста пользователя (только виды и длины), как везде.
- Коммиты без `Co-Authored-By`; git-команды автор набирает сам (однострочник с `cd ~/Documents/Claude/plug`). Не трогать `windows/`, `Data/switcher-rules.json`, `Data/tests/`.

## Review Focus

- Слой со смешанным оформлением (жирное слово посреди) — после правок оформление на месте, в т. ч. рядом с вставленным неразрывным пробелом. → тест диффа в Task 1 (правки локальные, не «весь текст»).
- Эмодзи и прочие символы вне BMP в тексте слоя — смещения правок в UTF-16 не съезжают. → тест в Task 1.
- Мягкий перенос ` ` и `\n` внутри слоя — сохраняются как есть. → тест в Task 3 (обработка пакета).
- Свитчер не запущен / перезапущен, пока плагин висит — плагин переподключается сам, без ошибок в консоли Фигмы. → проверка в Task 2.
- Два быстрых нажатия подряд — второе игнорируется, пока пакет в работе. → тест в Task 3.

---

### Task 1: Чистая логика — сообщения, правки, статистика (`ValloCore`)

**Files:**
- Create: `Sources/ValloCore/FigmaBridgeCore.swift`
- Test: `Tests/ValloCoreTests/FigmaBridgeCoreTests.swift`

**Interfaces — Produces:**
- `public enum FigmaMessage: Equatable` с `static func decode(_ data: Data) -> FigmaMessage?` и `func encode() -> Data`. Варианты (JSON-поле `type`):
  - плагин → свитчер: `.hello(version: Int, file: String)` (`{"type":"hello","version":1,"file":"…"}`); `.mode(id: Int, mode: FigmaMode, skipped: Int)` где `FigmaMode` = `.text | .empty | .layers([FigmaText])` (`{"type":"mode","id":1,"mode":"layers","texts":[{"id":"1:23","text":"…"}],"skipped":3}` — `skipped` = слои без шрифта, только для итога); `.done(id: Int)`.
  - свитчер → плагин: `.press(id: Int)`; `.result(id: Int, edits: [FigmaTextEdits], stats: FixStats, skipped: Int)`.
- `public struct FigmaText: Codable, Equatable { id: String; text: String }`
- `public struct TextEdit: Codable, Equatable { start: Int; end: Int; insert: String }` — смещения в UTF-16 (как строки JS), относительно **исходного** текста.
- `public struct FigmaTextEdits: Codable, Equatable { id: String; edits: [TextEdit] }`
- `public enum TextDiff { static func edits(from old: String, to new: String) -> [TextEdit] }` — по возрастанию `start`, без пересечений; пусто, если строки равны.
- `public struct FixStats: Codable, Equatable { commas, typos, yo, nbsp, dashes, quotes, other, layers: Int }` + `mutating func add(_:)`, `var isEmpty: Bool` (всё, кроме `layers`, ноль).
- `public enum FixCounter`: `static func commas(_ a: String, _ b: String) -> Int` (прирост `,`), `yo` (прирост ё/Ё), `typos` (число слов `a`, изменённых в `b`: удаления в `CollectionDifference` по массивам слов), `typography(_ a:_ b:) -> (nbsp, dashes, quotes, other)` (прирост ` `; `—–−`; `«»„“”‘’`; `…×©®™±→←½₽` соответственно; отрицательное → 0).

- [ ] **Step 1: Тесты**
  - `testHelloDecodes`: `{"type":"hello","version":1,"file":"Лендинг"}` → `.hello(version: 1, file: "Лендинг")`.
  - `testLayersModeDecodes`: сообщение `mode` с двумя текстами и `skipped: 3` → `.mode(id: 7, mode: .layers([...]), skipped: 3)`.
  - `testResultRoundTrip`: `.result` → `encode` → `decode` → то же значение.
  - `testGarbageIsNil`: `Data("{".utf8)` и `{"type":"zzz"}` → `nil`.
  - `testDiffEqualIsEmpty`: `edits(from: "abc", to: "abc") == []`.
  - `testDiffLocalNbsp`: `"в доме и в саду"` → `"в\u{00A0}доме и\u{00A0}в\u{00A0}саду"` — правок 3, каждая `end - start == 1`, `insert == "\u{00A0}"` (не одна большая правка).
  - `testDiffUtf16Offsets`: `"👍 \"да\""` → `"👍 «да»"`: правки на `start` 3 и 6 (эмодзи = 2 единицы UTF-16).
  - `testApplyReproducesNew`: хелпер в тесте применяет правки с конца к UTF-16 старой строки → получается новая; на 5 парах, в т. ч. вставка в начало, удаление в конце, полная замена.
  - `testCounters`: `commas("а б", "а, б") == 1`; `yo("еще", "ещё") == 1`; `typos("помоему тут", "по-моему тут") == 1`; `typography("a - b \"c\"", "a\u{00A0}— b «c»") == (nbsp: 1, dashes: 1, quotes: 2, other: 0)`.
- [ ] **Step 2:** `swift test --filter FigmaBridgeCoreTests` — падает (типов нет).
- [ ] **Step 3:** Реализовать. Диф — Myers или LCS по UTF-16 (тексты слоёв короткие; на строки длиннее 20 000 единиц — одна правка «всё», чтобы не зависнуть). Соседние операции склеивать в одну правку.
- [ ] **Step 4:** `swift test` — всё зелёное (≈146 старых + новые).
- [ ] **Step 5:** Коммит (однострочник автору): `cd ~/Documents/Claude/plug && git add Sources/ValloCore/FigmaBridgeCore.swift Tests/ValloCoreTests/FigmaBridgeCoreTests.swift && git commit -m "feat: figma bridge core — messages, utf16 diff, fix stats" && git push`

### Task 2: Связь — сервер в свитчере и скелет плагина (проверка рисков)

**Files:**
- Create: `Sources/ValloSwitcher/FigmaBridge.swift`
- Create: `figma-plugin/manifest.json`, `figma-plugin/code.js`, `figma-plugin/ui.html`, `figma-plugin/PROTOCOL.md`
- Modify: `Sources/ValloSwitcher/SettingsStore.swift` (ключ `figmaPluginSeen: Bool`, по умолчанию `false`)
- Modify: `Sources/ValloSwitcher/AppDelegate.swift` (создать и запустить мост)
- Modify: `Sources/ValloSwitcher/SettingsView.swift` (`TypographyTab`: секция «Фигма»)
- Modify: `scripts/build-app.sh` (копировать `figma-plugin/` в `Contents/Resources/figma-plugin/`)

**Interfaces:**
- Consumes: `FigmaMessage` (Task 1).
- Produces: `final class FigmaBridge` — `init(store: SettingsStore)`, `func start()`, `var connectedFiles: [String]`, `func send(_ m: FigmaMessage, toFile file: String) -> Bool`, `var onMessage: ((FigmaMessage, _ file: String) -> Void)?`. При `hello` — `store.figmaPluginSeen = true`. Всё на главном потоке.
- `manifest.json`: `"editorType": ["figma"]`, `"name": "Vallo"`, `"networkAccess": {"allowedDomains": ["ws://localhost:48731", "ws://127.0.0.1:48731"], "reasoning": "Плагин связывается с приложением Vallo Switcher на этом же компьютере: тексты обрабатываются в нём"}`, `"documentAccess": "dynamic-page"`.
- `ui.html`: WebSocket, переподключение раз в 2 с; пересылает сообщения `code.js` ↔ свитчер через `postMessage`. `code.js`: `figma.showUI(__html__, { visible: false })`, на подключение — `hello` с `figma.root.name`.
- Секция «Фигма» в `TypographyTab`: текст «Чтобы причёсывать фреймы целиком, установите плагин: в Фигме «Плагины → Разработка → Импорт плагина из манифеста» и выберите файл manifest.json.» (через `Typographer.format`) + кнопка «Показать плагин для Фигмы» → `NSWorkspace.shared.activateFileViewerSelecting([.../figma-plugin/manifest.json])`.

- [ ] **Step 1:** Сервер: `NWListener` на `127.0.0.1:48731`, WebSocket с `autoReplyPing`; отклонять рукопожатие с `Origin`, отличным от `null`. Порт занят — записать в журнал, без падения.
- [ ] **Step 2:** Плагин-скелет + `PROTOCOL.md` (поля из Task 1, порт, Origin, порядок: `hello` → `press` → `mode` → `result` → `done`).
- [ ] **Step 3:** Временная отладка (удалить в Task 4): в режиме отладки по двойному правому Shift в Фигме писать в журнал `connectedFiles`, заголовок окна Фигмы (`kAXFocusedWindow` → `kAXTitle`) и роль сфокусированного элемента (`ContextGuard.dumpFocusedElement`).
- [ ] **Step 4:** `swift test`, `scripts/install.sh`.
- [ ] **Step 5: Проверка с автором (контрольная точка, ответы — в план).** Автор импортирует плагин, запускает его в файле. Проверяем по журналу:
  1. связь есть (файл в `connectedFiles`); после перезапуска свитчера плагин переподключился сам;
  2. заголовок окна = имя файла (для нескольких вкладок);
  3. роль фокуса: холст с выделенным фреймом / редактирование текста в слое / переименование слоя — различимы?
  4. вручную: ⌘/ → «Vallo» → Enter находит плагин из разработки; работает ли ⌘/ при редактировании текста.
  Если 2 или 3 не различаются — решить с автором до Task 4 (запасной путь для 3: спросить плагин, `selectedTextRange`).
- [ ] **Step 6:** Коммит: `cd ~/Documents/Claude/plug && git add figma-plugin Sources/ValloSwitcher scripts/build-app.sh && git commit -m "feat: figma plugin bridge — local websocket server and plugin skeleton" && git push`

### Task 3: Плагин делает работу; свитчер обрабатывает пакет

**Files:**
- Create: `Sources/ValloSwitcher/FigmaBatch.swift`
- Modify: `figma-plugin/code.js`, `figma-plugin/ui.html`
- Test: `Tests/ValloCoreTests/FigmaBridgeCoreTests.swift` (добавить)

**Interfaces:**
- Consumes: Task 1, Task 2.
- Produces: `final class FigmaBatch` — `init(typos:yo:commas:store:)`, `var isRunning: Bool`, `func run(_ texts: [FigmaText], completion: @escaping ([FigmaTextEdits], FixStats) -> Void)`. По одному тексту по очереди: шаги из Global Constraints, счётчики после каждого шага (`FixCounter`), `layers` += 1, если текст изменился; в ответ — только изменённые. Чистую часть (всё, кроме модели) вынести в `ValloCore`: `public static func FixPipeline.finish(original:typoFixed:yoFixed:withCommas:) -> (String, FixStats)` — её и тестировать.
- Плагин на `press`: `selectedTextRange` → `mode: text`; пустое выделение → `mode: empty` + `figma.notify("Выделите фреймы или слои")`; иначе — `figma.notify("Vallo: типографирую N слоёв…", {timeout: Infinity})`, собрать `TEXT` из выделения (сам узел + `findAllWithCriteria({types: ["TEXT"]})`, без дублей по id), `hasMissingFont` — в `skipped`, остальные → `mode: layers`. Запомнить id выделения на момент нажатия.
- На `result`: для каждого слоя — `loadFontAsync` для всех шрифтов из `getStyledTextSegments(["fontName"])`, правки с конца: `deleteCharacters(start, end)` при `end > start`, `insertCharacters(start, insert, start > 0 ? "BEFORE" : "AFTER")`; затем `figma.commitUndo()`, убрать плашку, показать окошко (`figma.ui.show()`, `figma.ui.resize(240, h)`) и отправить `done`.
- Окошко (`ui.html`), стиль Фигмы (Inter 11/12, переменные темы `themeColors: true`): заголовок «Готово», строки по ненулевым: «Запятые — 4», «Опечатки — 2», «Ё — 3», «Неразрывные пробелы — 17», «Тире — 5», «Кавычки — 6», «Другие знаки — 1», «Слоёв изменено — 12», «Пропущено (нет шрифта) — 3»; всё ноль — «Всё уже в порядке, исправлять нечего»; кнопка «Готово» → `figma.ui.hide()`.

- [ ] **Step 1: Тесты `FixPipeline`**
  - `testPipelineKeepsCaseAndNoPeriod`: модельный ответ с заглавной и точкой не даёт их в результате (вход через `CommaFilter.merge(..., capitalize: false)`).
  - `testPipelineKeepsLineSeparators`: текст с `\n` и `\u{2028}` — оба на месте после `Typographer.format`.
  - `testPipelineStats`: «помоему еще - да» → статистика `typos 1, yo 1, dashes 1, nbsp ≥ 1`.
- [ ] **Step 2:** `swift test --filter FigmaBridgeCoreTests` — падает.
- [ ] **Step 3:** Реализовать `FixPipeline`, `FigmaBatch` (вызов `run`, пока `isRunning`, игнорируется: без `completion`, только строка в журнале), плагин.
- [ ] **Step 4:** `swift test` — зелёное.
- [ ] **Step 5:** Коммит: `cd ~/Documents/Claude/plug && git add Sources figma-plugin Tests && git commit -m "feat: figma plugin applies typography to selected layers with stats" && git push`

### Task 4: Двойной правый Shift в Фигме — маршрут, автозапуск, подсказка

**Files:**
- Create: `Sources/ValloSwitcher/FigmaLauncher.swift`
- Modify: `Sources/ValloSwitcher/KeyboardTap.swift` (`typographySelection()`)

**Interfaces:**
- Consumes: `FigmaBridge`, `FigmaBatch`, ответы проверки Task 2.
- Produces: `enum FigmaLauncher { static func launch() }` — ⌘/ (keyCode 44 + `.maskCommand`), пауза 0,3 с, набор «Vallo» через `EventPoster.insert`, пауза 0,4 с, Return (36).

- [ ] **Step 1:** В `typographySelection()` до `SelectionReader`: если спереди `com.figma.Desktop`, `store.figmaPluginSeen` и фокус не в текстовом поле (признак — из Task 2) →
  - файл спереди (заголовок окна) есть в `connectedFiles` → `press`; ответ `text` → старый путь; `layers` → `FigmaBatch.run` → `result`; `empty` → ничего;
  - нет → `FigmaLauncher.launch()`, ждать `hello` от этого файла до 4 с, затем `press`; не дождались → уведомление «Запустите плагин Vallo в Фигме вручную» (тот же `UNUserNotificationCenter`, что в `Updater.announceIfUpdated`).
  - Заголовок окна не определился → не трогаем ничего, та же подсказка.
  - Иначе (не Фигма или плагин ни разу не был на связи) — всё как сейчас.
- [ ] **Step 2:** Убрать временную отладку Task 2 (оставить строку журнала «figma: mode=…, texts=N, ms=…»).
- [ ] **Step 3:** `swift test`, `scripts/install.sh`.
- [ ] **Step 4: Ручная проверка с автором** — список из спеки («Проверка»), плюс: окошко статистики; «Всё уже в порядке»; два быстрых нажатия; эмодзи в слое; жирное слово рядом с предлогом.
- [ ] **Step 5:** Коммит: `cd ~/Documents/Claude/plug && git add Sources && git commit -m "feat: double right shift in figma typographs selected frames, auto-launches plugin" && git push`

### Task 5: Документы

**Files:** `Support/Help.html` (раздел «Фигма: фреймы целиком»), `docs/HANDOFF.md` (состояние, порт, протокол, что проверено), `README.md` (строка в возможностях), спека — статус «сделано».

- [ ] **Step 1:** Написать (тексты на «вы», через типографику; «автор», не «ученик»).
- [ ] **Step 2:** Финальное ревью свежим агентом всей ветки.
- [ ] **Step 3:** Коммит: `cd ~/Documents/Claude/plug && git add Support/Help.html docs README.md && git commit -m "docs: figma frames typography" && git push`
- Публикация в Figma Community — отдельно, после «работает у меня» от автора.
