#!/bin/zsh
# Собирает свитчер и ставит в «Программы», перезапускает.
# Запуск из корня репозитория: scripts/install.sh
set -euo pipefail

scripts/build-app.sh
pkill -f "Vallo Switcher.app/Contents/MacOS/ValloSwitcher" || true
sleep 0.5
rm -rf "/Applications/Vallo Switcher.app"
cp -R "build/Vallo Switcher.app" /Applications/
open "/Applications/Vallo Switcher.app"
echo "Установлено: /Applications/Vallo Switcher.app"
