#!/usr/bin/env bash
# Builds a Heimdall.app bundle for one macOS arch and packages it into a .dmg.
# macOS-only (uses sips/iconutil/codesign/hdiutil). Run from the repo root on a macOS runner:
#   build/macos/make-dmg.sh <rid> <version>
set -euo pipefail

RID="${1:?usage: make-dmg.sh <osx-x64|osx-arm64> <version>}"
VERSION="${2:?usage: make-dmg.sh <osx-x64|osx-arm64> <version>}"

case "$RID" in
    osx-x64) ARCH="x64" ;;
    osx-arm64) ARCH="arm64" ;;
    *) echo "Unsupported RID: $RID" >&2; exit 1 ;;
esac

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP="Heimdall.app"
CONTENTS="$APP/Contents"

rm -rf "$APP" dmg-staging publish "Heimdall.iconset"

# A .app is a folder, so publish self-contained (not single-file) straight into it.
dotnet publish "$ROOT/src/Heimdall/Heimdall.csproj" \
    -c Release -r "$RID" --self-contained \
    -p:Version="$VERSION" -o publish

mkdir -p "$CONTENTS/MacOS" "$CONTENTS/Resources"
cp -R publish/. "$CONTENTS/MacOS/"

# Info.plist with the version substituted in.
sed "s/__VERSION__/$VERSION/g" "$ROOT/build/macos/Info.plist" > "$CONTENTS/Info.plist"

# Build the .icns from the committed 1024px source.
ICONSET="Heimdall.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$ROOT/build/macos/Heimdall.png" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z "$double" "$double" "$ROOT/build/macos/Heimdall.png" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$CONTENTS/Resources/Heimdall.icns"

# Ad-hoc signature — required or Apple Silicon refuses to launch the binary at all.
codesign --force --deep --sign - "$APP"

# DMG with an /Applications drag-target.
mkdir -p dmg-staging
cp -R "$APP" dmg-staging/
ln -s /Applications dmg-staging/Applications

DMG="Heimdall-macos-${ARCH}-v${VERSION}.dmg"
hdiutil create -volname "Heimdall" -srcfolder dmg-staging -ov -format UDZO "$DMG"
echo "Created $DMG"
