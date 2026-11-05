#!/bin/zsh
# Пересобирает страницу окна настроек: settings-ui/ → Support/SettingsUI/index.html (готовая лежит в репозитории).
# Нужен Node. Запуск из корня репозитория: scripts/build-settings-ui.sh
set -euo pipefail
cd settings-ui
npm ci
npm run build
