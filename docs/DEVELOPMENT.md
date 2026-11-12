# Для разработчиков

Как собрать Vallo Switcher и как он устроен. Описание для пользователей — в [README](../README.md).

## Сборка на Маке

Нужен Xcode (Swift 6) и сертификат **Apple Development** в связке ключей (Xcode → Settings → Accounts → Manage Certificates).

```bash
swift test              # автотесты логики
scripts/build-app.sh    # build/Vallo Switcher.app
scripts/install.sh      # собрать и поставить в /Applications
scripts/package.sh      # собрать DMG для раздачи в build/
scripts/release.sh      # выпуск для автообновления: DMG + подпись + appcast.xml
scripts/build-settings-ui.sh # пересобрать окно настроек (settings-ui → Support/SettingsUI/index.html, нужен Node)
scripts/build-typo-dictionary.py путь/к/ruwiki.txt # пересобрать словарь опечаток
scripts/render-guide.sh # пересобрать «Как установить.pdf» из Support/Guide/guide.html (нужен Chrome)
```

Подпись берётся из `SIGN_ID` (по умолчанию `Apple Development`). Подпись стабильная, поэтому после пересборки Мак не спрашивает разрешения заново.

## Windows

Версия для Windows лежит в `windows/` (.NET), как собрать — [windows/README.md](../windows/README.md). Пакет `ValloSwitcher-win-x64` собирает GitHub Actions на каждый пуш.

Выпуск для Windows: написать «что нового» в `windows/Releases/<версия>.md` и запушить метку `win-v<версия>` (например, `win-v0.2`). GitHub Actions (`.github/workflows/windows-release.yml`) соберёт установщик Velopack (`ValloSwitcher-win-Setup.exe`) и выложит выпуск, а маковский выпуск останется «последним» — на него ведёт ссылка в README. Заодно CI кладёт список версий (`releases.win.json` с адресом папки выпуска) на служебную ветку `win-updates`: свитчеры на винде раз в день читают этот файл (`Updater.cs`, `UpdateFeed`), а если он не открылся — смотрят выпуски через API GitHub. Ветку `win-updates` руками не трогать: CI перезаписывает её при каждом выпуске. Версии у винды свои: `win-v0.1`, `win-v0.2`… Ссылки «Скачать для Windows» в README ведут на конкретный выпуск — после выпуска поменять в них номер.

## Устройство

- `Sources/ValloCore`: вся логика, без системных API, покрыта тестами. Переводчик раскладок, словари, судья (решает, ошибка ли это), движок (память набора и сценарии), детектор двойного Shift, типографика, опечатки.
- `Sources/ValloSwitcher`: приложение. Перехват клавиатуры, реальные раскладки системы, отправка нажатий, выделение и буфер обмена, исключения, меню, окно настроек, онбординг, автообновление (Sparkle).
- `settings-ui`: окно настроек — одна веб-страница (React + shadcn) для Мака (WKWebView) и Windows (WebView2). Протокол — `settings-ui/src/bridge.ts`.
- `figma-plugin`: плагин Фигмы (простой JS без сборки) — собирает тексты выделенных слоёв, применяет правки свитчера, показывает итог. Протокол — `figma-plugin/PROTOCOL.md`.
- `Sources/dump-layouts`: снимает раскладки системы в `Data/layouts/` для тестов.
- `Data/wordlists`: частотные словари ([hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords), CC-BY-SA-4.0).
- Запятые — модель SAGE (Сбер, MIT) через ONNX Runtime, скачивается из [vallo-models](https://github.com/madvito-collab/vallo-models). Ответ модели проходит через фильтр: берутся только запятые и безопасные исправления.

## Отладка

Включите «Режим отладки» в настройках и смотрите журнал:

```bash
/usr/bin/log stream --level debug --predicate 'subsystem == "com.vallo.switcher"'
```

В журнал пишутся только виды действий и длины, сам текст туда не попадает.
