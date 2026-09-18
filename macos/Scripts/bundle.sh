#!/bin/bash
# SPM ciktisini .app paketine cevirir. MIMARI.md bolum 1: paket uretimi
# zorunlu, cunku SMAppService ve UNUserNotificationCenter ciplak ikilide cokuyor.
# Kullanim: Scripts/bundle.sh [debug|release]
set -euo pipefail
CONF="${1:-debug}"
cd "$(dirname "$0")/.."

APP=".build/DockHub.app"
BIN=".build/$CONF/DockHubApp"
[ -f "$BIN" ] || { echo "Ikili bulunamadi: $BIN — once 'swift build' calistir." >&2; exit 1; }

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/DockHub"
cp Resources/Info.plist "$APP/Contents/Info.plist"

# Ad-hoc imza: ucretsiz, Apple ID gerektirmez. Gercek Developer ID icin
# Xcode'a Apple ID ile giris gerekir (bkz. docs/IMZALAMA.md).
codesign --force --sign - --identifier com.dockhub.mac "$APP" 2>/dev/null \
  && echo "  ad-hoc imzalandi" \
  || echo "  UYARI: ad-hoc imzalama basarisiz, imzasiz devam ediliyor"

echo "  hazir: $APP"
