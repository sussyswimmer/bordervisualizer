#!/usr/bin/env bash
# Builds the Mac app (docs/10-MACOS.md §1.5) on macOS with Xcode (or its command line tools) and a .NET 10 SDK:
#   1. publishes Core with NativeAOT for each architecture and joins them into libRimlightCore.dylib (.build-core/);
#   2. builds the Swift package against it;
#   3. assembles <AppName>.app, signs it ad hoc, and packs it in artifacts/mac/: <AppName>-mac.zip (the update archive
#      Sparkle downloads) and <AppName>Setup.dmg (the download, named like the Windows RimlightSetup.exe).
# The name and version come only from src/Directory.Build.props.
#
# Usage: mac/build.sh [--native]
#   --native   this Mac's architecture only (faster), instead of a universal arm64 + x86_64 build.
# Environment (the release workflow sets these; both optional):
#   RIMLIGHT_VERSION     the release's version, e.g. v1.2.0 (a leading "v" is dropped), instead of <Version>.
#   SPARKLE_PUBLIC_KEY   the update key's public half (base64). Without it the app has no updater.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
mac="$root/mac"
out="$root/artifacts/mac"
core_dir="$mac/.build-core"

archs=(arm64 x86_64)
if [[ "${1:-}" == "--native" ]]; then archs=("$(uname -m)"); fi

prop() { sed -n "s:.*<$1>\(.*\)</$1>.*:\1:p" "$root/src/Directory.Build.props" | head -n 1; }
app_name="$(prop AppName)"
version="${RIMLIGHT_VERSION:-$(prop Version)}"
version="${version#[vV]}"
if [[ -z "$app_name" || -z "$version" ]]; then
  echo "error: <AppName> or <Version> missing from src/Directory.Build.props" >&2
  exit 1
fi
if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
  echo "error: version '$version' is not like 1.2.0 or 1.3.0-beta.1" >&2
  exit 1
fi
feed_url="https://github.com/sussyswimmer/bordervisualizer/releases/latest/download/appcast.xml"
public_key="${SPARKLE_PUBLIC_KEY:-}"
if [[ -n "$public_key" && ! "$public_key" =~ ^[A-Za-z0-9+/]{43}=$ ]]; then
  echo "error: SPARKLE_PUBLIC_KEY is not a base64 Ed25519 public key" >&2
  exit 1
fi
bundle_version="${version%%-*}"
bundle_id="io.github.sussyswimmer.$(echo "$app_name" | tr '[:upper:]' '[:lower:]')"
echo "Building $app_name $version ($bundle_id) for ${archs[*]}"

# 1. Core, one NativeAOT dylib per architecture, joined.
rm -rf "$core_dir" "$out"
mkdir -p "$core_dir/publish" "$out"
slices=()
for arch in "${archs[@]}"; do
  case "$arch" in
    arm64) rid=osx-arm64 ;;
    x86_64) rid=osx-x64 ;;
    *) echo "error: unknown architecture $arch" >&2; exit 1 ;;
  esac
  dotnet publish "$root/src/Rimlight.Native/Rimlight.Native.csproj" -c Release -r "$rid" \
    -o "$core_dir/publish/$rid" -p:DebugType=none -p:Version="$version"
  slices+=("$core_dir/publish/$rid/Rimlight.Native.dylib")
done
lib="$core_dir/libRimlightCore.dylib"
lipo -create "${slices[@]}" -output "$lib"
install_name_tool -id "@rpath/libRimlightCore.dylib" "$lib"
codesign --force --sign - "$lib"

# 2. The Swift package, with warnings as errors.
arch_flags=()
for arch in "${archs[@]}"; do arch_flags+=(--arch "$arch"); done
swift_flags=(-c release --package-path "$mac" "${arch_flags[@]}" -Xswiftc -warnings-as-errors -Xcc -Werror)
swift build "${swift_flags[@]}"
bin_dir="$(swift build "${swift_flags[@]}" --show-bin-path)"

# 3. The bundle.
app="$out/$app_name.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Frameworks" "$app/Contents/Resources"
cp "$bin_dir/RimlightMac" "$app/Contents/MacOS/$app_name"
cp "$lib" "$app/Contents/Frameworks/"
# Sparkle as SwiftPM delivers it, signed by its authors; ditto keeps the framework's symlinks.
sparkle="$bin_dir/Sparkle.framework"
if [[ ! -d "$sparkle" ]]; then
  sparkle="$(find "$mac/.build/artifacts" -maxdepth 6 -path "*/macos-arm64_x86_64/Sparkle.framework" | head -n 1)"
fi
if [[ ! -d "$sparkle" ]]; then
  echo "error: Sparkle.framework not found in the Swift build" >&2
  exit 1
fi
ditto "$sparkle" "$app/Contents/Frameworks/Sparkle.framework"
# The development rpath to .build-core: the bundled app finds the dylib in Contents/Frameworks.
install_name_tool -delete_rpath "$core_dir" "$app/Contents/MacOS/$app_name"
if otool -l "$app/Contents/MacOS/$app_name" | grep -q ".build-core"; then
  echo "error: the app still has a path to .build-core in its load commands" >&2
  exit 1
fi
sed -e "s|@APP_NAME@|$app_name|g" -e "s|@BUNDLE_ID@|$bundle_id|g" -e "s|@VERSION@|$version|g" -e "s|@BUNDLE_VERSION@|$bundle_version|g" \
  -e "s|@FEED_URL@|$feed_url|g" -e "s|@SPARKLE_PUBLIC_KEY@|$public_key|g" \
  "$mac/Resources/Info.plist.in" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"

# The glow shader, with glow_math.h in place of its include line (runtime compilation can't resolve includes).
awk -v header="$mac/Sources/CRimlight/include/glow_math.h" \
  '$0 == "#include \"glow_math.h\"" { while ((getline line < header) > 0) print line; next } { print }' \
  "$mac/Resources/Shaders/Glow.metal" > "$app/Contents/Resources/Glow.metal"
if grep -q '#include "glow_math.h"' "$app/Contents/Resources/Glow.metal"; then
  echo "error: glow_math.h wasn't joined into Glow.metal" >&2
  exit 1
fi

# The app icon, from the repo's 512 px icon.
iconset="$out/AppIcon.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$root/assets/icon-512.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  if [[ $double -le 512 ]]; then
    sips -z "$double" "$double" "$root/assets/icon-512.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
  fi
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"
rm -rf "$iconset"

# 4. Ad-hoc signature, inside out (no Developer ID: docs/10-MACOS.md §6). Sparkle keeps its own signature: the app has
#    no hardened runtime, so library validation doesn't ask for one team.
codesign --force --sign - "$app/Contents/Frameworks/libRimlightCore.dylib"
codesign --force --sign - "$app"
codesign --verify --strict --deep --verbose=2 "$app"

# 5. The update archive: a zip that keeps the bundle's permissions and symlinks.
ditto -c -k --keepParent "$app" "$out/$app_name-mac.zip"

# 6. The download: a compressed disk image with the app and a shortcut to Applications.
staging="$out/dmg"
mkdir -p "$staging"
ditto "$app" "$staging/$app_name.app"
ln -s /Applications "$staging/Applications"
hdiutil create -volname "$app_name" -srcfolder "$staging" -fs HFS+ -format UDZO -ov "$out/${app_name}Setup.dmg"
rm -rf "$staging"
hdiutil verify "$out/${app_name}Setup.dmg"
echo "Built $app ($version), $out/$app_name-mac.zip and $out/${app_name}Setup.dmg"
