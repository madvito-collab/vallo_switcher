# Опечатки и запятые — план работ

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Свой исправлятор опечаток (на лету и по двойному правому Shift) и необязательная скачиваемая модель для запятых с фильтром, который не даёт ей ломать живую речь.

**Architecture:** Исправлятор опечаток — чистая логика в `ValloCore` на словаре свитчера (теперь с частотами). На лету он встроен в тот же конвейер, что и типографика (`LiveTypographer`): одна правка на пробел, одна отмена через Backspace. Модель SAGE экспортируется в ONNX, ужимается и кладётся в публичный репозиторий; приложение скачивает её по кнопке и запускает через ONNX Runtime со своим циклом генерации. Ответ модели проходит через фильтр в `ValloCore` (только запятые и безопасные замены).

**Tech Stack:** Swift 6, SwiftPM, Swift Testing; ONNX Runtime (`microsoft/onnxruntime-swift-package-manager`), токенизатор `huggingface/swift-transformers` (Tokenizers); Python + `optimum` только для разового экспорта модели (в scratchpad, не в репозитории).

**Spec:** `docs/superpowers/specs/2026-10-30-typos-commas-design.md`

## Global Constraints

- Исправлятор: кириллица, длина ≥ 5; кандидаты — 20 000 частых слов с частотой ≥ 200; уверенность — единственный кандидат или лучший ≥ 10× второго; слово из хвоста 20 001–50 000 — только если кандидат ≥ 300× его частоты. Правки: лишняя буква/задвоение (не последняя), пропущенная (не перед последней), перестановка соседних (включая две последние). Замены буквы на букву нет.
- **Регистр не меняем никогда**: исправленное слово получает регистр исходного («Првет» → «Привет», «ПРВЕТ» → «ПРИВЕТ»).
- Частицы для дефиса: `то нибудь либо таки` («какой то» → «какой-то», «ктото» → «кто-то»).
- Защищены: разговорные словечки (`Judge.slang`), «Мои слова» (`typoIgnoredWords`).
- Порядок на лету: свитчер → опечатки → типографика; если свитчер проглотил нажатие — остальные молчат.
- Модель: `ai-forever/sage-fredt5-distilled-95m` (проверить лицензию на карточке модели перед публикацией), ONNX, квантование int8, цель ≤ 150 МБ. Хранится в релизе `madvito-collab/vallo-models`, скачивается в `~/Library/Application Support/Vallo Switcher/Models/`.
- Фильтр модели: принимаем `, ? ! : ;` после слов; замену слова — только если её принял бы исправлятор опечаток, или е↔ё, или дефис в частице; добавленные/удалённые слова — никогда; точку между цифрами — никогда; заглавную в начале и точку в конце — только при `typoCapitalizeEnabled`.
- Новые ключи UserDefaults: `typoLiveEnabled` (true), `typoIgnoredWords` ([String]), `typoCapitalizeEnabled` (false). Набранный текст по-прежнему не хранится и не логируется; сеть — только скачивание модели по кнопке.
- Коммиты без `Co-Authored-By`.

## Review Focus

1. **Опечатка на лету и свитчер на одном пробеле** (`yfgbie` → «напишу» свитчером, затем пробел) — исправлятор не должен трогать слово, которое только что исправил свитчер. Ручная проверка Task 4.
2. **Замена меняет длину слова** («приввет» → «привет») — память свитчера должна сброситься, иначе откат двойным Shift сотрёт не то. Тест `typoEditChangesLength` в Task 3 + ручная проверка Task 4.
3. **Модель долго думает или падает** — двойной правый Shift не должен вешать клавиатуру (генерация не на главном потоке), при ошибке модели применяются опечатки и типографика без запятых. Ручная проверка Task 7.
4. **Битый или недокачанный файл модели** — свитчер не падает, модель считается «не скачана», кнопка «Скачать» снова доступна. Ручная проверка Task 8.
5. **Имена и бренды с заглавной** («Сбербанк», «Виталий», «Фигма», «Яндекс») — не исправляются. Тест `namesAndBrandsKept` в Task 1.

---

## Структура файлов

```
Sources/ValloCore/Lexicon.swift          ← + частоты слов, ранги
Sources/ValloCore/TypoCorrector.swift    ← исправлятор опечаток (слово, фраза)
Sources/ValloCore/CommaFilter.swift      ← фильтр ответа модели
Sources/ValloCore/LiveTypographer.swift  ← + шаг опечаток в конвейере на лету
Sources/ValloSwitcher/CommaModel.swift   ← ONNX Runtime + токенизатор + цикл генерации
Sources/ValloSwitcher/ModelStore.swift   ← скачивание/удаление/статус модели
Sources/ValloSwitcher/SettingsView.swift ← вкладка «Опечатки», «Слова» в два раздела
scripts/export-model.py                  ← разовый экспорт SAGE → ONNX int8 (запускается в scratchpad-venv)
Tests/ValloCoreTests/TypoCorrectorTests.swift, CommaFilterTests.swift, LiveTypographerTests.swift (+)
Data/test/sage-outputs.tsv               ← ответы SAGE из эксперимента для тестов фильтра
```

---

### Task 1: Словарь с частотами и исправлятор опечаток

**Files:** Modify `Sources/ValloCore/Lexicon.swift`; Create `Sources/ValloCore/TypoCorrector.swift`; Test `Tests/ValloCoreTests/TypoCorrectorTests.swift`

**Interfaces — Produces:**
```swift
// Lexicon: + частоты
public func frequency(_ s: String) -> Int          // 0 — нет в словаре
public func rank(_ s: String) -> Int?              // 0-based место в списке
public static func load(url:language:limit:) — limit по умолчанию остаётся 20_000 для судьи;
// исправлятору нужен список на 50 000: Lexicon.load(url:, language: .ru, limit: 50_000)

public struct TypoCorrector: Sendable {
    public init(lexicon: Lexicon, ignored: Set<String>)   // lexicon — 50k с частотами
    public var ignored: Set<String>                        // «Мои слова», нормализованы
    public func correct(_ word: String) -> String?         // nil — не трогать
    public func correctPhrase(_ text: String) -> String    // все слова + дефисы частиц
}
```
- [ ] **Step 1: Тесты** (на реальном `Data/wordlists/ru.txt`):
  - `fixesCommonTypos`: `првет, приввет, привте` → `привет`; `пожалуста` → `пожалуйста`; `сегодян` → `сегодня`; `спасиббо` → `спасибо`; `нормалньо` → `нормально`.
  - `keepsCase`: `Првет` → `Привет`, `ПРВЕТ` → `ПРИВЕТ`.
  - `keepsSlangItAndRare`: каждое из контрольного списка эксперимента (88 слов: фигма, мокап, задеплоил, пушнуть, засинкаемся, ща, хз, кринж, онбординг, кейсы, прототипы…) → `nil`; `планку`, `уговорю` → `nil`.
  - `namesAndBrandsKept`: `Сбербанк, Виталий, Фигма, Яндекс, Телеграм, Озон, Авито, Пушкин, Москва` → `nil`.
  - `hyphenParticles`: `correctPhrase("какой то баг ктото")` == `"какой-то баг кто-то"`.
  - `ignoredWordsKept`: `TypoCorrector(…, ignored: ["приввет"]).correct("приввет") == nil`.
  - `syntheticTypoQuality`: генератор промахов из эксперимента (seed 7, 150 слов с 300-го по 6000-е место) — верно ≥ 80%, неверно ≤ 2.
  - `rareWordsSafety`: 3000 слов с 20 001–50 000 мест (seed 7) — испорчено ≤ 0,5%.
- [ ] **Step 2:** `swift test --filter TypoCorrectorTests` → FAIL (`TypoCorrector` не определён).
- [ ] **Step 3:** Lexicon хранит `[String: Int]` частот (из файла) и порядок; `TypoCorrector` — алгоритм эксперимента (`scratchpad/spike/typo.py`) по правилам из Global Constraints.
- [ ] **Step 4:** `swift test` → PASS. Замер: `correct` на слово < 2 мс (печатать в тесте).
- [ ] **Step 5:** Коммит `feat: typo corrector`.

### Task 2: Фильтр ответа модели

**Files:** Create `Sources/ValloCore/CommaFilter.swift`, `Data/test/sage-outputs.tsv` (из эксперимента: исходная фраза ⇥ ответ SAGE); Test `Tests/ValloCoreTests/CommaFilterTests.swift`

**Interfaces — Consumes:** `TypoCorrector`. **Produces:**
```swift
public enum CommaFilter {
    public static func merge(original: String, model: String, typos: TypoCorrector, capitalize: Bool) -> String
}
```
Алгоритм — `scratchpad/spike/filter.py` (сопоставление слов через выравнивание, ключ: строчные, ё→е, без дефиса), но условие замены слова — «`typos.correct(original) == new`» вместо проверки macOS.
- [ ] **Step 1: Тесты** на `sage-outputs.tsv`: `хз` остаётся (не «хочу»), `ща` остаётся, `спс` остаётся, `задеплоил` остаётся, `скрин` остаётся; `"я думаю что это хорошая идея"` → `"я думаю, что это хорошая идея"`; `"когда ты придешь мы пойдем гулять"` → `"когда ты придёшь, мы пойдём гулять"`; `"у нас митинг в 15 00"` без изменений; `"вобщем всё нормально"` → `"в общем, всё нормально"` (если исправлятор примет «вобщем»; иначе — `"вобщем, всё нормально"`, зафиксировать фактическое); `capitalize: false` — нет точки в конце и строчная в начале; `capitalize: true` — как у модели.
- [ ] **Step 2:** FAIL → реализовать → `swift test` PASS.
- [ ] **Step 3:** Коммит `feat: comma filter`.

### Task 3: Опечатки в конвейере на лету

**Files:** Modify `Sources/ValloCore/LiveTypographer.swift`; Test `Tests/ValloCoreTests/LiveTypographerTests.swift`

**Interfaces — Produces:**
```swift
public struct LiveOptions: Sendable { public var typography: Bool; public var typos: TypoCorrector? }
// LiveTypographer.typed(_ char:, hint:, options: LiveOptions = .init(typography: true, typos: nil))
// LiveTypographer: public private(set) var undoneTypoWord: String?   // после backspace(): слово, которое человек вернул
```
На границе слова (символ — не буква), если `options.typos` есть: последнее слово в `raw` перед границей прогоняется через `correct`, затем (если `typography`) — `Typographer.format`. Отмена возвращает набранное целиком; если в отменённой правке было исправление опечатки — `undoneTypoWord` = исходное слово (нормализованное).
- [ ] **Step 1: Тесты:** `typoOnSpace` (`"првет "` → правка `delete: 5, insert: "привет "`); `typoAndTypographyTogether` (`"в првет "` → `"в привет "` в итоге контекста); `typoUndoTeachesWord` (после `"приввет "` `backspace()` → правка-отмена и `undoneTypoWord == "приввет"`); `typoOffKeepsWord` (`typos: nil` → `"првет "` без правки); `typoEditChangesLength` (`"приввет "` → `lastEditChangedLength == true`).
- [ ] **Step 2:** FAIL → реализовать → `swift test` PASS.
- [ ] **Step 3:** Коммит `feat: live typo correction`.

### Task 4: Подключение опечаток в приложении и «Мои слова»

**Files:** Modify `KeyboardTap.swift`, `SettingsStore.swift`, `AppDelegate.swift`, `SettingsView.swift`

**Produces:** `SettingsStore.typoLiveEnabled`, `typoIgnoredWords`, `typoCapitalizeEnabled`; `KeyboardTap.typos: TypoCorrector` (загружается из `wordlists/ru.txt` с limit 50 000 при старте); `live.typed(…, options:)` с `typos: store.typoLiveEnabled ? typos : nil`, `typography: store.typographyLiveEnabled`; живой путь работает, если включено хоть что-то из двух и приложение не в исключениях типографики. После `live.backspace()` с `undoneTypoWord` — добавить слово в `typoIgnoredWords` и в `typos.ignored`. Вкладка «Слова»: разделы «Не переключать раскладку» и «Не исправлять». Раздел исключений переименовать в «Типографика и опечатки на лету».
- [ ] **Step 1:** Реализовать, `swift build -c release` и `swift test` PASS.
- [ ] **Step 2: Ручная проверка с учеником** (TextEdit, Telegram, Chrome): `првет`, `приввет`, `пожалуста`, `ктото` + пробел; Backspace сразу после — вернулось и слово появилось в «Не исправлять»; `фигма`, `хз`, `Сбербанк` не трогаются; **Review Focus 1, 2**: `yfgbie` + пробел, затем двойной левый Shift — текст не портится.
- [ ] **Step 3:** Коммит `feat: typos in the app, my words`.

### Task 5: Экспорт и публикация модели

**Files:** Create `scripts/export-model.py` (запускается в scratchpad-venv, не в сборке)

- [ ] **Step 1:** Проверить лицензию `ai-forever/sage-fredt5-distilled-95m` на Hugging Face; записать в `Data/models/README.md` (название, лицензия, источник, размер).
- [ ] **Step 2:** `optimum-cli export onnx --model ai-forever/sage-fredt5-distilled-95m --task text2text-generation-with-past` → encoder/decoder ONNX; квантование int8 (`optimum.onnxruntime.ORTQuantizer`, dynamic). Ожидается: суммарно ≤ 150 МБ.
- [ ] **Step 3:** Сравнить в Python ответы int8 и исходной модели на 34 фразах эксперимента; расхождений по запятым ≤ 2.
- [ ] **Step 4:** Упаковать `encoder.onnx`, `decoder.onnx`, `tokenizer.json`, `config.json`, `LICENSE` в `sage-fredt5-95m-int8.zip`, посчитать SHA-256. **Перед загрузкой спросить ученика**, затем выложить в релиз `models-1` репозитория `madvito-collab/vallo-models` (через `git push` тега и загрузку файла — ученик создаёт репозиторий заранее).
- [ ] **Step 5:** Коммит `chore: model export script`.

### Task 6: Модель в Swift (ONNX Runtime)

**Files:** Modify `Package.swift` (зависимости `onnxruntime-swift-package-manager`, `swift-transformers` → product `Tokenizers`); Create `Sources/ValloSwitcher/CommaModel.swift`

**Produces:**
```swift
final class CommaModel {
    init(directory: URL) throws                     // папка распакованной модели
    func run(_ text: String) throws -> String       // жадная генерация, max 128 токенов
}
```
- [ ] **Step 1:** Разведка: собрать CLI-цель `model-check` (временно), прогнать 5 фраз, сравнить с Python int8. Ожидается: совпадение текста; время ≤ 0,5 с на фразу. Если ONNX Runtime или токенизатор не собираются под нашу сборку — **стоп, спросить ученика** (запасной путь — Core ML).
- [ ] **Step 2:** Реализовать `CommaModel` (кэш `past_key_values`, жадный поиск, стоп по eos). Удалить `model-check`.
- [ ] **Step 3:** Проверить, что `scripts/build-app.sh` кладёт библиотеки ONNX Runtime в `.app` и подпись проходит (`codesign --verify --strict`).
- [ ] **Step 4:** Коммит `feat: comma model runtime`.

### Task 7: Двойной правый Shift — всё сразу

**Files:** Modify `KeyboardTap.swift`

Порядок по выделенному тексту: `typos.correctPhrase` → (если модель загружена) `CommaModel.run` на фоне + `CommaFilter.merge(…, typos:, capitalize: store.typoCapitalizeEnabled)` → `Typographer.format` → `replaceSelection`. Модель грузится лениво при первом использовании; ошибка или таймаут 3 с → без запятых. Пока работает — ничего не блокирует главный поток.
- [ ] **Step 1:** Реализовать, сборка и тесты PASS.
- [ ] **Step 2: Ручная проверка** (**Review Focus 3**): выделить `короче я хз что делать првет как дела` → двойной правый Shift: без модели `короче я хз что делать привет как дела`; с моделью — плюс запятые, `хз` на месте; во время работы модели можно печатать.
- [ ] **Step 3:** Коммит `feat: right shift fixes typos, commas, typography`.

### Task 8: Скачивание модели и вкладка «Опечатки»

**Files:** Create `Sources/ValloSwitcher/ModelStore.swift`; Modify `SettingsView.swift`, `OnboardingView.swift`

**Produces:**
```swift
final class ModelStore: ObservableObject {
    enum State { case notDownloaded, downloading(Double), ready, failed(String) }
    @Published private(set) var state: State
    func download()      // URLSession, проверка SHA-256, распаковка в Application Support
    func delete()
    var directory: URL?  // если ready
}
```
Вкладка «Опечатки»: «Исправлять опечатки на лету»; блок «Запятые по сочетанию» (название модели, размер, статус, «Скачать» / «Удалить», полоса загрузки); «Заглавная буква и точка в конце»; примеры «что исправляется / что нет»; предупреждение про автозамену macOS (`NSAutomaticSpellingCorrectionEnabled` в глобальном домене: нет ключа или true → показать) с кнопкой «Открыть настройки клавиатуры» (`x-apple.systempreferences:com.apple.Keyboard-Settings.extension`). Онбординг, финальный шаг: строка про автозамену macOS.
- [ ] **Step 1:** Реализовать, сборка PASS.
- [ ] **Step 2: Ручная проверка** (**Review Focus 4**): скачать модель, статус «Активна»; удалить; прервать скачивание (выключить Wi-Fi) — состояние «Ошибка», повтор работает; подменить файл мусором — свитчер не падает, предлагает скачать заново.
- [ ] **Step 3:** Коммит `feat: model download and typos tab`.

### Task 9: Финал

- [ ] **Step 1:** `swift test` — всё PASS.
- [ ] **Step 2:** Справка (`Support/Help.html`), PDF (`Support/Guide/guide.html` → `scripts/render-guide.sh`), README: опечатки, запятые, «Мои слова», совет выключить автозамену macOS. Версия 0.3.
- [ ] **Step 3:** Финальное ревью свежим проверяющим, исправления Critical/Important с тестами.
- [ ] **Step 4:** `scripts/install.sh`, `scripts/package.sh`, коммит, пуш, метка `v0.3`.
