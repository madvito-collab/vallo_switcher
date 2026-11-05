# Окно настроек на вебе (shadcn) — одно на Мак и Windows

Дата: 2026-11-05. Утверждённый автором мокап — `docs/mockups/settings.html`.

## Зачем

Окно настроек должно выглядеть одинаково на Маке и Windows и быть понятным: боковое меню, страница справа, карточки «название + короткая подпись + переключатель/кнопка», как в настройках ChatGPT. Делать два родных окна (SwiftUI и WinUI) — двойная работа и неизбежный разнобой. Одна веб-страница внутри окна приложения — один код, одни тексты.

## Решения автора

- Структура по мокапу: **Основное · Раскладка · Исправления**, группа **Интеграции → Figma**, внизу **Справка** и **О программе**. Поиска нет — пунктов мало.
- Каждая настройка в одном месте. «Требует внимания» на «Основном» — только пока есть проблема (модель запятых не скачана; мешает автозамена macOS).
- «О программе» — страница, а не окно: версия, ссылка на GitHub (Telegram автора не показываем), обновления, модель запятых.
- Справка — короткая, в карточках, прямо в окне; полная (`Help.html`) — по ссылке.
- В «Раскладка → Не переключать слова» слова можно добавлять руками, как в «Исправлениях».
- Тексты — короткие, без точки в конце подписей и заголовков; «macOS», а не «Мак».
- shadcn/ui, иконки Lucide, акцент — системный цвет (по умолчанию синий), светлая и тёмная тема по системе.

## Как устроено

- `settings-ui/` — React + TypeScript + Vite + Tailwind + компоненты shadcn (Switch, Select, Button, Input) + lucide-react. Сборка кладёт готовую страницу в `Support/SettingsUI/` (один `index.html` со встроенными JS и CSS). Готовая сборка лежит в репозитории: свитчеру и CI на Windows Node не нужен; пересборка — `scripts/build-settings-ui.sh`.
- Страница ничего не хранит. Всё знает и применяет приложение; страница показывает его состояние и шлёт изменения.
- Мак: `WKWebView` в обычном окне (`SettingsWindow`), мост `SettingsBridge`. Windows: `WebView2`, тот же протокол (следующий этап).
- Меню у часов: «Справка», «О программе», «Скачать модель запятых» открывают окно на нужной странице.

## Протокол страница ↔ приложение

Страница → приложение (`window.webkit.messageHandlers.vallo` на Маке, `window.chrome.webview` на Windows), JSON:

- `{type: "ready"}` — страница загрузилась, пришли состояние.
- `{type: "set", key, value}` — изменить настройку. Ключи: `launchAtLogin`, `debugMode`, `autoSwitch`, `typography`, `typos`, `yo`, `capitalize`, `autoUpdate` (bool); `hotkey.convert`, `hotkey.correct` (id варианта); `apps.layout`, `apps.correct`, `words.layout`, `words.correct` (массив строк).
- `{type: "action", name, arg?}` — `pickApps` (arg: `layout`|`correct`), `addWord` (arg: `{list, word}`), `checkUpdates`, `model.download`, `model.cancel`, `model.delete`, `openKeyboardSettings`, `openFigmaPlugin`, `openGitHub`, `openFullHelp`, `cancelRecording`.

Приложение → страница: `window.vallo.receive(state)` целиком при каждом изменении. Состояние:

```
platform: "mac" | "windows", version, page (страница, на которой открыть окно — или null)
general: { launchAtLogin, debugMode }
attention: { modelMissing, macAutocorrect }
layout: { autoSwitch, hotkey, apps: [{id, name, icon}], words: [] }
correct: { available, hotkey, capitalize, typography, typos, yo, apps, words }
hotkeyOptions: [{ id, title }]      // "double:leftShift"…, "custom" (своё — заголовок текущего), "record", "off" (только для исправлений)
recording: "convert" | "correct" | null, hotkeyError: { action, text } | null
integrations: { figma: bool }
about: { autoUpdate, lastCheck (ISO или null), model: { state, progress, message, size, disk } }
```

`icon` — PNG значка приложения (data URL, 32 px). Окно показывает только то, что есть на платформе: на Windows пока нет исправлений и Figma — этих страниц там нет.

## Не цели

- Онбординг, меню у часов, окошко итога Figma остаются родными.
- Окно на Windows — следующий этап, после проверки на Маке.

## Оставлено исполнителю

Вёрстка компонентов по мокапу; размеры окна (около 900×640, растягивается); разбиение React-кода на файлы.
