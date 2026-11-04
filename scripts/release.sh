#!/bin/zsh
# Готовит выпуск для автообновления: собирает DMG, подписывает его ключом Sparkle
# и дописывает новую версию в appcast.xml (список версий, который читают все свитчеры).
# «Что нового» — HTML-кусок в Support/Releases/<версия>.html, пишется до запуска.
# Запуск из корня репозитория: scripts/release.sh
# Сам выпуск на GitHub — командой, которую скрипт печатает в конце.
set -euo pipefail

VERSION=$(/usr/libexec/PlistBuddy -c "Print CFBundleShortVersionString" Support/Info.plist)
BUILD=$(/usr/libexec/PlistBuddy -c "Print CFBundleVersion" Support/Info.plist)
DMG="build/Vallo-Switcher-$VERSION.dmg"
NOTES="Support/Releases/$VERSION.html"
SPARKLE_VERSION="2.10.0"
TOOLS="build/sparkle/bin"

[ -f "$NOTES" ] || { echo "Нет «что нового»: $NOTES" >&2; exit 1; }
grep -q "<sparkle:version>$BUILD</sparkle:version>" appcast.xml && { echo "Версия $BUILD уже есть в appcast.xml — подними CFBundleVersion" >&2; exit 1; }

# инструменты Sparkle (подпись обновления) — скачиваем один раз
if [ ! -x "$TOOLS/sign_update" ]; then
    mkdir -p build/sparkle
    curl -sL -o build/sparkle/sparkle.tar.xz \
        "https://github.com/sparkle-project/Sparkle/releases/download/$SPARKLE_VERSION/Sparkle-$SPARKLE_VERSION.tar.xz"
    tar xf build/sparkle/sparkle.tar.xz -C build/sparkle
fi

scripts/package.sh

# подпись ключом из «Связки ключей»: вида sparkle:edSignature="..." length="..."
SIGNATURE=$("$TOOLS/sign_update" "$DMG")

python3 - "$VERSION" "$BUILD" "$SIGNATURE" "$NOTES" <<'EOF'
import sys, email.utils
version, build, signature, notes = sys.argv[1:]
url = f"https://github.com/madvito-collab/vallo_switcher/releases/download/v{version}/Vallo-Switcher-{version}.dmg"
item = f"""        <item>
            <title>Версия {version}</title>
            <pubDate>{email.utils.formatdate(localtime=True)}</pubDate>
            <sparkle:version>{build}</sparkle:version>
            <sparkle:shortVersionString>{version}</sparkle:shortVersionString>
            <sparkle:minimumSystemVersion>14.0</sparkle:minimumSystemVersion>
            <description><![CDATA[
{open(notes, encoding="utf-8").read().strip()}
            ]]></description>
            <enclosure url="{url}" {signature} type="application/octet-stream"/>
        </item>
"""
s = open("appcast.xml", encoding="utf-8").read()
marker = "<!-- новые версии — сверху -->\n"
open("appcast.xml", "w", encoding="utf-8").write(s.replace(marker, marker + item, 1))
EOF

echo
echo "Готово: $DMG подписан, версия $VERSION дописана в appcast.xml."
echo "Выпустить (набрать самому):"
echo "cd ~/Documents/Claude/plug && git add -A && git commit -m \"release: v$VERSION\" && git tag v$VERSION && git push origin v$VERSION && gh release create v$VERSION $DMG --title \"Vallo Switcher $VERSION\" --notes-file $NOTES && git push"
