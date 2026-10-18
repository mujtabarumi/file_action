#!/usr/bin/env bash
# Wraps a published macOS build into FileAction.app and zips it:
#   packaging/make-mac-app.sh <version> <publish-dir> [out-dir] [arch]
# e.g. packaging/make-mac-app.sh 1.3.0 publish-mac dist arm64  →  dist/FileAction-1.3.0-macos-arm64.zip
# The bundle is ad-hoc signed (no Developer ID), so the first launch needs right-click → Open, or
#   xattr -dr com.apple.quarantine FileAction.app
set -euo pipefail
version="$1"
publish="$2"
out="${3:-dist}"
arch="${4:-arm64}"
root="$(cd "$(dirname "$0")/.." && pwd)"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

app="$stage/FileAction.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$out"
cp -a "$publish/." "$app/Contents/MacOS/"
cp "$root/THIRD-PARTY-NOTICES.md" "$app/Contents/Resources/"
sed -e "s|@VERSION@|$version|g" "$root/packaging/mac/Info.plist.in" > "$app/Contents/Info.plist"
printf 'APPL????' > "$app/Contents/PkgInfo"

# The icon: an .icns made from the PNG the Linux package uses (sips/iconutil ship with macOS).
iconset="$stage/fileaction.iconset"
mkdir -p "$iconset"
png="$root/src/FileAction.Desktop/Assets/fileaction.png"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$png" --out "$iconset/icon_${size}x${size}.png" > /dev/null
  sips -z "$((size * 2))" "$((size * 2))" "$png" --out "$iconset/icon_${size}x${size}@2x.png" > /dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/fileaction.icns"

# Ad-hoc signature: without one macOS refuses to start the app at all on Apple silicon.
codesign --force --deep --sign - "$app" > /dev/null 2>&1

zip="FileAction-$version-macos-$arch.zip"
rm -f "$out/$zip"
ditto -c -k --keepParent "$app" "$out/$zip"
shasum -a 256 "$out/$zip" | cut -d' ' -f1 > "$out/$zip.sha256"
echo "$out/$zip"
