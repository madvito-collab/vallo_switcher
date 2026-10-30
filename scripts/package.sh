#!/bin/zsh
# Собирает DMG для коллег: приложение, ярлык «Программы» и инструкция.
# Запуск из корня репозитория: scripts/package.sh
set -euo pipefail

VERSION=$(/usr/libexec/PlistBuddy -c "Print CFBundleShortVersionString" Support/Info.plist)
DMG="build/Vallo-Switcher-$VERSION.dmg"
STAGE="build/dmg"

scripts/build-app.sh
rm -rf "$STAGE" "$DMG"
mkdir -p "$STAGE"
cp -R "build/Vallo Switcher.app" "$STAGE/"
ln -s /Applications "$STAGE/Программы"
cp "Support/Guide/Как установить.pdf" "$STAGE/Как установить.pdf"

hdiutil create -volname "Vallo Switcher $VERSION" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
echo "Готово: $DMG"
