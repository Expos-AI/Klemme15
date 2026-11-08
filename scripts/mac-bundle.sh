#!/usr/bin/env bash
# Klemme15: open-source BMW diagnostics
# Copyright (C) 2026 Kindric Castro
# SPDX-License-Identifier: GPL-3.0-or-later
#
# Turns a published Mac build into Klemme15.app, so Mac users get a normal app they can drag to Applications and
# open with a double-click, with the Klemme15 icon. Runs on the macOS release runner.
#
#   scripts/mac-bundle.sh <publish folder> <version>
#
# The folder must hold the published Klemme15 executable and EdiabasLib.config; they move into the bundle. The bundle
# is signed ad hoc (Apple Silicon refuses unsigned code); it isn't notarised, so the first launch needs
# "Open Anyway" in System Settings, which the README explains.
set -euo pipefail

out="$1"
version="${2#v}"
here="$(cd "$(dirname "$0")/.." && pwd)"
app="$out/Klemme15.app"

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
mv "$out/Klemme15" "$app/Contents/MacOS/Klemme15"
mv "$out/EdiabasLib.config" "$app/Contents/MacOS/EdiabasLib.config"

# The icon, from the brand PNG (1024 x 1024), at every size macOS asks for.
iconset="$(mktemp -d)/Klemme15.iconset"
mkdir -p "$iconset"
src="$here/docs/brand/klemme15-icon.png"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$src" --out "$iconset/icon_${size}x${size}.png" > /dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$src" --out "$iconset/icon_${size}x${size}@2x.png" > /dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/Klemme15.icns"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Klemme15</string>
  <key>CFBundleDisplayName</key><string>Klemme15</string>
  <key>CFBundleIdentifier</key><string>io.github.expos-ai.klemme15</string>
  <key>CFBundleExecutable</key><string>Klemme15</string>
  <key>CFBundleIconFile</key><string>Klemme15</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${version}</string>
  <key>CFBundleVersion</key><string>${version}</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>Copyright (C) 2026 Kindric Castro. GPL-3.0-or-later.</string>
</dict>
</plist>
PLIST

# No extended attributes (quarantine, provenance) in the bundle: zipped, they'd come out as stray "._" files that
# break the signature when the zip is opened with anything but Archive Utility.
xattr -cr "$app"
codesign --force --deep --sign - "$app"
codesign --verify --deep "$app"
echo "Built $app ($version)"
