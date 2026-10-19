#!/usr/bin/env bash
# Builds the Mac app (docs/10-MACOS.md §1.5) on macOS with Xcode (or its command line tools) and a .NET 10 SDK:
#   1. publishes Core with NativeAOT for each architecture and joins them into libRimlightCore.dylib (.build-core/);
#   2. builds the Swift package against it;
#   3. assembles <AppName>.app, signs it ad hoc, and zips it, in artifacts/mac/.
# The name and version come only from src/Directory.Build.props.
#
# Usage: mac/build.sh [--native]
#   --native   this Mac's architecture only (faster), instead of a universal arm64 + x86_64 build.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
mac="$root/mac"
out="$root/artifacts/mac"
core_dir="$mac/.build-core"

archs=(arm64 x86_64)
if [[ "${1:-}" == "--native" ]]; then archs=("$(uname -m)"); fi

prop() { sed -n "s:.*<$1>\(.*\)</$1>.*:\1:p" "$root/src/Directory.Build.props" | head -n 1; }
app_name="$(prop AppName)"
version="$(prop Version)"
if [[ -z "$app_name" || -z "$version" ]]; then
  echo "error: <AppName> or <Version> missing from src/Directory.Build.props" >&2
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
    -o "$core_dir/publish/$rid" -p:DebugType=none
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
# The development rpath to .build-core: the bundled app finds the dylib in Contents/Frameworks.
install_name_tool -delete_rpath "$core_dir" "$app/Contents/MacOS/$app_name"
if otool -l "$app/Contents/MacOS/$app_name" | grep -q ".build-core"; then
  echo "error: the app still has a path to .build-core in its load commands" >&2
  exit 1
fi
sed -e "s|@APP_NAME@|$app_name|g" -e "s|@BUNDLE_ID@|$bundle_id|g" -e "s|@VERSION@|$version|g" -e "s|@BUNDLE_VERSION@|$bundle_version|g" \
  "$mac/Resources/Info.plist.in" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"

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

# 4. Ad-hoc signature, inside out (no Developer ID: docs/10-MACOS.md §6).
codesign --force --sign - "$app/Contents/Frameworks/libRimlightCore.dylib"
codesign --force --sign - "$app"
codesign --verify --strict --verbose=2 "$app"

# 5. A zip that keeps the bundle's permissions and symlinks.
ditto -c -k --keepParent "$app" "$out/$app_name-mac.zip"
echo "Built $app and $out/$app_name-mac.zip"
