#!/bin/zsh
# Превращает Support/Guide/guide.html в Support/Guide/Как установить.pdf через Chrome без окна.
# Запуск из корня репозитория: scripts/render-guide.sh
set -euo pipefail
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
"$CHROME" --headless=new --disable-gpu --no-pdf-header-footer \
    --print-to-pdf="$PWD/Support/Guide/Как установить.pdf" "file://$PWD/Support/Guide/guide.html" 2>/dev/null
echo "Готово: Support/Guide/Как установить.pdf"
