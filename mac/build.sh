#!/bin/bash
# Builds Glass.app for macOS — no Xcode project needed, just the Command Line Tools.
#   chmod +x build.sh && ./build.sh
set -euo pipefail
cd "$(dirname "$0")"

APP="Glass.app"
MIN_OS="14.0"

if ! command -v swiftc >/dev/null 2>&1; then
  echo "Swift compiler not found. Install the tools with:  xcode-select --install"
  exit 1
fi

echo "→ Closing any running copy…"
pkill -x Glass 2>/dev/null || true

echo "→ Compiling ($(swiftc --version 2>&1 | head -n1))"
rm -rf build "$APP"
mkdir -p build

ARCHS=(arm64 x86_64)
for arch in "${ARCHS[@]}"; do
  swiftc -O -parse-as-library -swift-version 5 \
    -target "${arch}-apple-macos${MIN_OS}" \
    -framework AppKit -framework SwiftUI -framework Carbon -framework Security \
    Sources/*.swift -o "build/Glass-${arch}"
done
lipo -create build/Glass-arm64 build/Glass-x86_64 -output build/Glass   # universal: Apple silicon + Intel

echo "→ Packaging ${APP}"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp build/Glass "$APP/Contents/MacOS/Glass"
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Glass</string>
  <key>CFBundleDisplayName</key><string>Glass</string>
  <key>CFBundleIdentifier</key><string>com.glass.sidebar</string>
  <key>CFBundleExecutable</key><string>Glass</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>${MIN_OS}</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

# ad-hoc signature so macOS will run it locally and Keychain access works
codesign --force --deep --sign - "$APP" >/dev/null

echo "✓ Built $(pwd)/$APP — launching."
open "$APP"
