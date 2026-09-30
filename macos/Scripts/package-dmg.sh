#!/bin/bash
# DockHub icin .app'i .dmg disk imajina paketler.
# Kullanim: Scripts/package-dmg.sh [debug|release]
set -euo pipefail
CONF="${1:-release}"
cd "$(dirname "$0")/.."

# Once .app paketini olustur
./Scripts/bundle.sh "$CONF"

APP=".build/DockHub.app"
DMG_NAME="DockHub.dmg"
DMG_OUT=".build/$DMG_NAME"
STAGING=".build/dmg-staging"

echo "DMG hazirlaniyor: $DMG_OUT..."
rm -rf "$STAGING" "$DMG_OUT"
mkdir -p "$STAGING"

# Uygulamayi kopyala ve /Applications kisayolunu ekle
cp -R "$APP" "$STAGING/DockHub.app"
ln -s /Applications "$STAGING/Applications"

# Opsiyonel: Kod imzalama (Developer ID)
if [ -n "${APPLE_DEVELOPER_ID:-}" ]; then
  echo "Developer ID ile imzalanıyor: $APPLE_DEVELOPER_ID"
  codesign --force --options runtime --timestamp \
    --sign "$APPLE_DEVELOPER_ID" "$STAGING/DockHub.app"
else
  echo "APPLE_DEVELOPER_ID tanimli degil, ad-hoc imza korundu."
fi

# DMG olustur
hdiutil create -volname "DockHub" \
  -srcfolder "$STAGING" \
  -ov -format UDZO \
  "$DMG_OUT"

# Opsiyonel: Notarization
if [ -n "${APPLE_ID:-}" ] && [ -n "${APPLE_PASSWORD:-}" ] && [ -n "${APPLE_TEAM_ID:-}" ]; then
  echo "Apple Notary servisine gonderiliyor..."
  xcrun notarytool submit "$DMG_OUT" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --wait
  xcrun stapler staple "$DMG_OUT"
  echo "Notarization tamamlandi ve ticket eklendi."
else
  echo "Apple Developer hesabi bilgileri (APPLE_ID vb.) tanimli degil, notarization atlaniyor."
fi

rm -rf "$STAGING"
echo "Bitti: $DMG_OUT"
