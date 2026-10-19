#!/usr/bin/env bash
# Writes the Sparkle appcast for one release (docs/10-MACOS.md §6): a single item pointing at the release's update
# zip, with its EdDSA signature. The release workflow attaches it as appcast.xml, so every copy's feed,
# releases/latest/download/appcast.xml, always names the newest release. Prereleases never become "latest", so they
# are never offered, as on Windows (prerelease: false).
#
# Usage: mac/appcast.sh <sign_update> <update zip> <version> <download url> > appcast.xml
#   sign_update    Sparkle's signing tool (bin/sign_update from its release archive).
#   The private key comes on standard input, so it never appears in an argument list or a file.
set -euo pipefail

if [[ $# -ne 4 ]]; then
  echo "usage: $0 <sign_update> <update zip> <version> <download url> < private-key" >&2
  exit 2
fi
sign_update="$1"
zip="$2"
version="${3#[vV]}"
url="$4"

root="$(cd "$(dirname "$0")/.." && pwd)"
app_name="$(sed -n 's:.*<AppName>\(.*\)</AppName>.*:\1:p' "$root/src/Directory.Build.props" | head -n 1)"
minimum="$(sed -n '/<key>LSMinimumSystemVersion<\/key>/{n;s:.*<string>\(.*\)</string>.*:\1:p;}' "$root/mac/Resources/Info.plist.in")"
if [[ -z "$app_name" || -z "$minimum" ]]; then
  echo "error: no <AppName> or LSMinimumSystemVersion" >&2
  exit 1
fi
if [[ "$url" != https://* ]]; then
  echo "error: the download URL must be https" >&2
  exit 1
fi

# sign_update prints: sparkle:edSignature="…" length="…"
signature="$("$sign_update" --ed-key-file - "$zip")"
if ! [[ "$signature" =~ ^sparkle:edSignature=\"[A-Za-z0-9+/=]+\"\ length=\"[0-9]+\"$ ]]; then
  echo "error: sign_update gave no signature" >&2
  exit 1
fi

cat <<EOF
<?xml version="1.0" encoding="utf-8"?>
<rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">
  <channel>
    <title>$app_name</title>
    <item>
      <title>$app_name $version</title>
      <pubDate>$(LC_ALL=C date -u '+%a, %d %b %Y %H:%M:%S +0000')</pubDate>
      <sparkle:version>${version%%-*}</sparkle:version>
      <sparkle:shortVersionString>$version</sparkle:shortVersionString>
      <sparkle:minimumSystemVersion>$minimum</sparkle:minimumSystemVersion>
      <enclosure url="$url" type="application/octet-stream" $signature />
    </item>
  </channel>
</rss>
EOF
