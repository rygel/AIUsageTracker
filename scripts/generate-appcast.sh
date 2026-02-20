#!/usr/bin/env bash
set -euo pipefail

version="${1:-}"
if [[ -z "$version" ]]; then
  echo "Usage: scripts/generate-appcast.sh <version>"
  exit 2
fi

version_number="${version#v}"
pub_date=$(date -u '+%a, %d %b %Y %H:%M:%S %z')
release_base_url="https://github.com/rygel/AIConsumptionTracker/releases"
download_base_url="$release_base_url/download/v$version_number"

generate_appcast() {
  local arch_suffix="$1"
  local filename="$2"

  echo '<?xml version="1.0" encoding="UTF-8"?>' > "$filename"
  echo '<rss xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle" version="2.0">' >> "$filename"
  echo '    <channel>' >> "$filename"
  echo '        <title>AI Consumption Tracker</title>' >> "$filename"
  echo "        <link>$release_base_url</link>" >> "$filename"
  echo '        <description>Most recent changes with links to updates.</description>' >> "$filename"
  echo '        <language>en</language>' >> "$filename"
  echo '        <item>' >> "$filename"
  echo "            <title>Version $version</title>" >> "$filename"
  echo "            <sparkle:releaseNotesLink>$release_base_url/tag/v$version_number</sparkle:releaseNotesLink>" >> "$filename"
  echo "            <pubDate>$pub_date</pubDate>" >> "$filename"
  echo "            <enclosure url=\"$download_base_url/AIConsumptionTracker_Setup_v${version_number}${arch_suffix}.exe\"" >> "$filename"
  echo "                       sparkle:version=\"$version_number\"" >> "$filename"
  echo '                       sparkle:os="windows"' >> "$filename"
  echo '                       length="0"' >> "$filename"
  echo '                       type="application/octet-stream" />' >> "$filename"
  echo '        </item>' >> "$filename"
  echo '    </channel>' >> "$filename"
  echo '</rss>' >> "$filename"
  echo "✓ Generated $filename"
}

generate_appcast "_win-x64" "appcast.xml"
generate_appcast "_win-x64" "appcast_x64.xml"
generate_appcast "_win-arm64" "appcast_arm64.xml"
generate_appcast "_win-x86" "appcast_x86.xml"

echo ""
echo "Generated 4 appcast files for version $version"
