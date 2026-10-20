#!/usr/bin/env bash
# CI's check of the release's Mac update signing (ci.yml, macos job): with a throwaway key pair and Sparkle's real
# sign_update, mac/appcast.sh must sign a stand-in update, and must refuse when the public key doesn't match.
# Usage: mac/tools/check-appcast.sh <sign_update>
set -euo pipefail

sign_update="$1"
root="$(cd "$(dirname "$0")/../.." && pwd)"
app_name="$(sed -n 's:.*<AppName>\(.*\)</AppName>.*:\1:p' "$root/src/Directory.Build.props" | head -n 1)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

keys=()
while IFS= read -r line; do keys+=("$line"); done < <(swift "$root/mac/tools/test-keypair.swift")
other=()
while IFS= read -r line; do other+=("$line"); done < <(swift "$root/mac/tools/test-keypair.swift")

stand_in() { # <public key> <zip>
  rm -rf "$work/app"
  mkdir -p "$work/app/$app_name.app/Contents"
  /usr/libexec/PlistBuddy -c "Add :SUPublicEDKey string $1" "$work/app/$app_name.app/Contents/Info.plist" >/dev/null
  ditto -c -k --sequesterRsrc --keepParent "$work/app/$app_name.app" "$2"
}
url="https://example.invalid/$app_name-mac.zip"

stand_in "${keys[1]}" "$work/good.zip"
echo "${keys[0]}" | SPARKLE_PUBLIC_KEY="${keys[1]}" "$root/mac/appcast.sh" "$sign_update" "$work/good.zip" v9.9.9 "$url" \
  > "$work/appcast.xml"
xmllint --noout "$work/appcast.xml"
grep -q 'sparkle:edSignature=' "$work/appcast.xml"
echo "ok: a matching key pair signs"

if echo "${keys[0]}" | SPARKLE_PUBLIC_KEY="${other[1]}" "$root/mac/appcast.sh" "$sign_update" "$work/good.zip" v9.9.9 \
  "$url" > /dev/null; then
  echo "error: a public key that isn't the pair's was accepted" >&2
  exit 1
fi
echo "ok: a public key from another pair is refused"

stand_in "${other[1]}" "$work/other.zip"
if echo "${keys[0]}" | SPARKLE_PUBLIC_KEY="${keys[1]}" "$root/mac/appcast.sh" "$sign_update" "$work/other.zip" v9.9.9 \
  "$url" > /dev/null; then
  echo "error: an app built with another key was accepted" >&2
  exit 1
fi
echo "ok: an app built with another key is refused"
