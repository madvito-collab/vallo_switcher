#!/bin/zsh
# Собирает build/Vallo Switcher.app и подписывает стабильной подписью,
# чтобы Мак не спрашивал разрешения заново после каждой пересборки.
# Запуск из корня репозитория: scripts/build-app.sh
set -euo pipefail

APP="build/Vallo Switcher.app"
SIGN_ID="${SIGN_ID:-Apple Development}"

swift build -c release --product ValloSwitcher
BIN="$(swift build -c release --show-bin-path)/ValloSwitcher"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/wordlists" "$APP/Contents/Frameworks"
cp "$BIN" "$APP/Contents/MacOS/ValloSwitcher"
# Sparkle (автообновление). ditto — чтобы не сломать символьные ссылки внутри фреймворка.
# XPC-сервисы нужны только приложениям в песочнице, у нас её нет — выкидываем.
ditto "$(dirname "$BIN")/Sparkle.framework" "$APP/Contents/Frameworks/Sparkle.framework"
rm -rf "$APP/Contents/Frameworks/Sparkle.framework/Versions/B/XPCServices" "$APP/Contents/Frameworks/Sparkle.framework/XPCServices"
# Библиотеки Swift, которых нет в старых macOS (Xcode кладёт их в приложение сам, SwiftPM — нет).
# Без libswiftCompatibilitySpan свитчер падал при запуске на macOS 14 (найдено при проверке).
SWIFT_LIBS="$(dirname "$(xcrun --find swift)")/../lib/swift-6.2/macosx"
for lib in $(otool -L "$APP/Contents/MacOS/ValloSwitcher" | awk '/@rpath\/libswift.*\.dylib/ {sub("@rpath/", "", $1); print $1}'); do
    [ -f "$SWIFT_LIBS/$lib" ] || { echo "Нет $lib в $SWIFT_LIBS — приложение не запустится на старых macOS" >&2; exit 1; }
    cp "$SWIFT_LIBS/$lib" "$APP/Contents/Frameworks/"
done
# SwiftPM записывает в бинарник «собрано под SDK 14», и macOS рисует старый дизайн.
# Ставим реальную версию SDK — тогда включается Liquid Glass.
# `--sdk macosx` обязателен: без него xcrun иногда не находит SDK и отдаёт пустоту — бинарник получал «sdk n/a»,
# и приложение молча теряло Liquid Glass (2026-11-02). Пустая версия — ошибка сборки, не тихая поломка.
SDK_VERSION="$(xcrun --sdk macosx --show-sdk-version)"
[ -n "$SDK_VERSION" ] || { echo "Не узнал версию SDK — без неё не будет Liquid Glass" >&2; exit 1; }
xcrun vtool -set-build-version macos 14.0 "$SDK_VERSION" -replace \
    -output "$APP/Contents/MacOS/ValloSwitcher" "$APP/Contents/MacOS/ValloSwitcher"
cp Support/Info.plist "$APP/Contents/Info.plist"
cp Data/wordlists/en.txt Data/wordlists/ru.txt Data/wordlists/ru-typos.txt Data/wordlists/ru-yo.txt Data/wordlists/ru-modern.txt Data/wordlists/eyo-LICENSE.txt Data/wordlists/README.md "$APP/Contents/Resources/wordlists/"
cp Data/switcher-rules.json "$APP/Contents/Resources/wordlists/"
# все формы русских слов (OpenCorpora): в репозитории сжаты, в приложении — распакованы (свитчер ищет прямо в файле)
gzip -dc Data/wordlists/ru-forms.txt.gz > "$APP/Contents/Resources/wordlists/ru-forms.txt"
cp Support/Icons/AppIcon.icns Support/Icons/MenuBarIcon.png Support/Icons/MenuBarIcon@2x.png "$APP/Contents/Resources/"
cp Support/Help.html "$APP/Contents/Resources/"
# окно настроек — готовая страница (исходники settings-ui/, пересборка — scripts/build-settings-ui.sh)
mkdir -p "$APP/Contents/Resources/SettingsUI"
cp Support/SettingsUI/index.html "$APP/Contents/Resources/SettingsUI/"
# плагин для Фигмы: человек импортирует manifest.json из «Настройки → Типографика»
mkdir -p "$APP/Contents/Resources/figma-plugin"
cp figma-plugin/manifest.json figma-plugin/code.js figma-plugin/ui.html "$APP/Contents/Resources/figma-plugin/"

# подписываем изнутри наружу: сначала помощники Sparkle, потом фреймворк, потом приложение
SPARKLE="$APP/Contents/Frameworks/Sparkle.framework/Versions/B"
codesign --force --options runtime --sign "$SIGN_ID" "$SPARKLE/Autoupdate"
codesign --force --options runtime --sign "$SIGN_ID" "$SPARKLE/Updater.app"
codesign --force --options runtime --sign "$SIGN_ID" "$APP/Contents/Frameworks/Sparkle.framework"
for lib in "$APP"/Contents/Frameworks/*.dylib(N); do codesign --force --options runtime --sign "$SIGN_ID" "$lib"; done
codesign --force --options runtime --sign "$SIGN_ID" "$APP"
echo "Готово: $APP"
