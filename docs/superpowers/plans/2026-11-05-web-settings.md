# Окно настроек на вебе — план (Мак)

**Spec:** `docs/superpowers/specs/2026-11-05-web-settings-design.md`. Мокап — `docs/mockups/settings.html`.
Исполнение — в этой же сессии (правки небольшие и связанные, проверка — вживую у автора).

## Global Constraints

- Тексты дословно из мокапа; без точки в конце подписей и заголовков; «macOS», не «Мак».
- Telegram автора в окне не показываем.
- Набранный текст никуда не уходит; страница не ходит в сеть (всё встроено в `index.html`).
- Node нужен только для пересборки страницы; свитчер собирается без него.

## Задачи

1. **Страница `settings-ui/`.** Vite + React + TS + Tailwind 4 + shadcn (switch, select, button, input), lucide-react, `vite-plugin-singlefile` → `Support/SettingsUI/index.html`. Страницы и тексты по мокапу. `bridge.ts`: отправка (webkit/webview2), приём `window.vallo.receive`, без приложения — пример состояния (для `npm run dev`). Проверка: `npm run build` без ошибок; страница в браузере выглядит как мокап.
2. **Мост на Маке.** `SettingsBridge.swift`: `WKScriptMessageHandler`, разбор `set`/`action`, сборка состояния из `SettingsModel`/`ModelStore`/`Updater`, отправка при изменениях (Combine). `SettingsWindow` — `WKWebView` вместо SwiftUI; старые страницы SwiftUI удаляются. Ручное добавление слов в «Не переключать» (`Lexicon.normalize`). Запись своего сочетания — `HotkeyRecorder`, состояние `recording`.
3. **Сборка.** `build-app.sh` кладёт `Support/SettingsUI/index.html` в ресурсы; `scripts/build-settings-ui.sh`. Справка: ссылки на страницы окна.
4. **Проверка.** `swift test`; живая проверка всех страниц, переключателей, списков, сочетаний, модели; скриншоты автору.
