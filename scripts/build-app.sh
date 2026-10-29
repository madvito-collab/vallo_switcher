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
cp Support/Info.plist "$APP/Contents/Info.plist"
cp Data/wordlists/en.txt Data/wordlists/ru.txt "$APP/Contents/Resources/wordlists/"

codesign --force --options runtime --sign "$SIGN_ID" "$APP"
echo "Готово: $APP"
