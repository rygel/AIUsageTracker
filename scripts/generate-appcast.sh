#!/bin/bash
set -e

# AI Usage Tracker - Appcast Generator
# Usage: ./scripts/generate-appcast.sh <version> [channel]
# Example: ./scripts/generate-appcast.sh 2.3.0-beta.1 beta

VERSION="$1"
CHANNEL="${2:-stable}"
REPOSITORY_BASE_URL="https://github.com/rygel/AIUsageTracker"

if [ -z "$VERSION" ]; then
    echo "Error: Version is required"
    echo "Usage: $0 <version> [channel]"
    echo "Example: $0 2.3.0-beta.1 beta"
    exit 1
fi

# Validate channel
if [ "$CHANNEL" != "stable" ] && [ "$CHANNEL" != "beta" ]; then
    echo "Error: Channel must be 'stable' or 'beta'"
    exit 1
fi

# Determine appcast prefix based on channel
if [ "$CHANNEL" == "stable" ]; then
    APPCAST_PREFIX="appcast"
    CHANNEL_TITLE="AI Usage Tracker"
    CHANNEL_DESC="Most recent changes with links to updates."
else
    APPCAST_PREFIX="appcast_beta"
    CHANNEL_TITLE="AI Usage Tracker (Beta Channel)"
    CHANNEL_DESC="Beta releases with latest features and improvements."
fi

# Create appcast directory if it doesn't exist
mkdir -p appcast

# Remove previously generated files for this channel so no stale feed survives uploads.
rm -f "appcast/${APPCAST_PREFIX}.xml" \
      "appcast/${APPCAST_PREFIX}_x64.xml" \
      "appcast/${APPCAST_PREFIX}_x86.xml" \
      "appcast/${APPCAST_PREFIX}_arm64.xml"

# Get current date in RFC 2822 format
PUB_DATE=$(date -u +"%a, %d %b %Y %H:%M:%S +0000")

# Generate appcast for each architecture
generate_appcast() {
    local arch=$1
    local suffix=""
    
    if [ "$arch" != "x64" ]; then
        suffix="_$arch"
    fi
    
    local appcast_file="appcast/${APPCAST_PREFIX}${suffix}.xml"
    local arch_title=""
    local arch_desc=""
    
    case "$arch" in
        x64)
            arch_title="${CHANNEL_TITLE}"
            arch_desc="${CHANNEL_DESC}"
            ;;
        x86)
            arch_title="${CHANNEL_TITLE} - x86"
            arch_desc="${CHANNEL_DESC%%.} for x86 architecture."
            ;;
        arm64)
            arch_title="${CHANNEL_TITLE} - ARM64"
            arch_desc="${CHANNEL_DESC%%.} for ARM64 architecture."
            ;;
    esac
    
    # Build sparkle:version — must be strictly increasing across releases so
    # Sparkle/NetSparkle can detect updates.  For beta releases we encode the
    # pre-release number as a 4th component (e.g. 2.3.4-beta.8 → 2.3.4.8) so
    # that beta.8 > beta.7 even though both share the same clean core version.
    local clean_version="${VERSION%%-*}"
    local sparkle_version="${clean_version}"
    if [[ "${VERSION}" =~ -beta\.([0-9]+)$ ]]; then
        sparkle_version="${clean_version}.${BASH_REMATCH[1]}"
    fi
    
    # Build download URL
    local download_url="${REPOSITORY_BASE_URL}/releases/download/v${VERSION}/AIUsageTracker_Setup_v${VERSION}_win-${arch}.exe"
    
    # Generate appcast XML
    cat > "$appcast_file" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<rss xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle" version="2.0">
    <channel>
        <title>${arch_title}</title>
        <link>${REPOSITORY_BASE_URL}/releases</link>
        <description>${arch_desc}</description>
        <language>en</language>
        <item>
            <title>Version ${VERSION}</title>
            <sparkle:releaseNotesLink>${REPOSITORY_BASE_URL}/releases/tag/v${VERSION}</sparkle:releaseNotesLink>
            <pubDate>${PUB_DATE}</pubDate>
            <enclosure url="${download_url}"
                       sparkle:version="${sparkle_version}"
                       sparkle:shortVersionString="${VERSION}"
                       sparkle:os="windows"
                       length="0"
                       type="application/octet-stream" />
        </item>
    </channel>
</rss>
EOF
    
    echo "✓ Generated ${appcast_file}"

    if [ "$arch" == "x64" ]; then
        local x64_appcast_file="appcast/${APPCAST_PREFIX}_x64.xml"
        cp "$appcast_file" "$x64_appcast_file"
        echo "✓ Generated ${x64_appcast_file}"
    fi
}

# Generate for each architecture
for arch in x64 x86 arm64; do
    generate_appcast "$arch"
done

echo ""
echo "=========================================="
echo "Appcast generation complete!"
echo "Channel: ${CHANNEL}"
echo "Version: ${VERSION}"
echo "=========================================="
echo ""
echo "Generated files:"
ls -la appcast/${APPCAST_PREFIX}*.xml
echo ""
