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
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/wordlists"
cp "$BIN" "$APP/Contents/MacOS/ValloSwitcher"
# SwiftPM записывает в бинарник «собрано под SDK 14», и macOS рисует старый дизайн.
# Ставим реальную версию SDK — тогда включается Liquid Glass.
xcrun vtool -set-build-version macos 14.0 "$(xcrun --show-sdk-version)" -replace \
    -output "$APP/Contents/MacOS/ValloSwitcher" "$APP/Contents/MacOS/ValloSwitcher"
cp Support/Info.plist "$APP/Contents/Info.plist"
cp Data/wordlists/en.txt Data/wordlists/ru.txt Data/wordlists/README.md "$APP/Contents/Resources/wordlists/"
cp Support/Icons/AppIcon.icns Support/Icons/MenuBarIcon.png Support/Icons/MenuBarIcon@2x.png "$APP/Contents/Resources/"

codesign --force --options runtime --sign "$SIGN_ID" "$APP"
echo "Готово: $APP"
