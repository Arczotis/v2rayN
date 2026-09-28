#!/bin/bash
# Local macOS build: publish v2rayN.Desktop (Avalonia) and assemble an unsigned/ad-hoc
# signed v2rayN.app bundle. Mirrors package-osx.sh but skips create-dmg so it can run
# on a developer machine without extra tooling.
#
#   ./build-macos.sh [arm64|x64] [Release|Debug]
set -euo pipefail

Arch="${1:-arm64}"
Config="${2:-Release}"

Root="$(cd "$(dirname "$0")" && pwd)"
Version="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$Root/v2rayN/Directory.Build.props" | head -1)"

Dist="$Root/dist"
Output="$Dist/v2rayN-macos-$Arch"
Package="$Dist/v2rayN-Package-macos-$Arch"
App="$Package/v2rayN.app"

export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# GlobalHotKeys is a git submodule; a plain clone leaves it empty and the build fails.
if [ -d "$Root/.git" ] && [ ! -f "$Root/v2rayN/GlobalHotKeys/src/GlobalHotKeys/GlobalHotKeys.csproj" ]; then
    echo "==> init submodules"
    git -C "$Root" submodule update --init v2rayN/GlobalHotKeys
fi

rm -rf "$Output" "$Package"
mkdir -p "$Output"

echo "==> publish (osx-$Arch, $Config)"
dotnet publish "$Root/v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj" \
    -c "$Config" -r "osx-$Arch" -p:SelfContained=true -o "$Output"

echo "==> core binaries"
CoreZip="v2rayN-macos-$Arch.zip"
if [ ! -f "$Dist/$CoreZip" ]; then
    # Set https_proxy if github.com is not directly reachable.
    curl -fL -o "$Dist/$CoreZip" \
        "https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/$CoreZip"
fi
unzip -oq "$Dist/$CoreZip" -d "$Dist/core-$Arch"
cp -rf "$Dist/core-$Arch/v2rayN-macos-$Arch/"* "$Output/"

echo "==> app bundle"
mkdir -p "$App/Contents/Resources"
cp -rf "$Output" "$App/Contents/MacOS"
cp -f "$App/Contents/MacOS/v2rayN.icns" "$App/Contents/Resources/AppIcon.icns"
echo "When this file exists, app will not store configs under this folder" >"$App/Contents/MacOS/NotStoreConfigHere.txt"
chmod +x "$App/Contents/MacOS/v2rayN"

cat >"$App/Contents/Info.plist" <<-EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleDisplayName</key>
  <string>v2rayN</string>
  <key>CFBundleExecutable</key>
  <string>v2rayN</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundleIconName</key>
  <string>AppIcon</string>
  <key>CFBundleIdentifier</key>
  <string>2dust.v2rayN.local</string>
  <key>CFBundleName</key>
  <string>v2rayN</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>${Version}</string>
  <key>CSResourcesFileMapped</key>
  <true/>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>LSMinimumSystemVersion</key>
  <string>13.6</string>
</dict>
</plist>
EOF

echo "==> ad-hoc codesign"
codesign --force --deep --sign - "$App"

echo "==> done: $App"
