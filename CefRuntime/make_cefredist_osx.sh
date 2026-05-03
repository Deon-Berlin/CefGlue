#!/usr/bin/env bash
# Read version from central config if $2 is not set
if [ -z "$2" ]; then
    SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    VERSION_FILE="${SCRIPT_DIR}/../cef-version.json"
    if [ -f "$VERSION_FILE" ]; then
        CEF_BUILD_VERSION=$(grep -o '"cef_build_version": *"[^"]*"' "$VERSION_FILE" | sed 's/"cef_build_version": *"//;s/"//')
    else
        echo "ERROR: cef-version.json not found at $VERSION_FILE"
        exit 1
    fi
else
    CEF_BUILD_VERSION="$2"
fi

# Extract version number (part before first +)
CEF_VERSION="${CEF_BUILD_VERSION%%+*}"

BASE="redist"
if [ ! -d "$BASE" ]; then
    mkdir "$BASE"
fi

TMP="${BASE}/tmp-$1-$CEF_VERSION"

if [ "$1" == "osx-x64" ]; then
    ARCH="macosx64";
else
    ARCH="macosarm64";
fi

if [ ! -d "$TMP" ]; then
    mkdir "$TMP"
fi

CEFZIP="${TMP}/cef-${CEF_VERSION}.tar.bz2"
CEFBINARIES="${TMP}/cef_binaries-${CEF_VERSION}"

if [ ! -f "$CEFZIP" ]; then
    echo "Downloading CEF binaries v${CEF_VERSION}-${ARCH}"
    curl -4 -o "$CEFZIP" "https://cef-builds.spotifycdn.com/cef_binary_${CEF_BUILD_VERSION//+/%2B}_${ARCH}_client.tar.bz2"
else
    echo "CEF binaries v${CEF_VERSION}-${ARCH} already downloaded"
fi

if [ ! -d "$CEFBINARIES" ]; then
    echo "Extracting CEF binaries v${CEF_VERSION}-${ARCH}"
    mkdir "$CEFBINARIES"
    tar -jxvf "$CEFZIP" -C "$CEFBINARIES"
else 
    echo "CEF binaries v${CEF_VERSION}-${ARCH} already extracted"
fi

OUTPUT="${BASE}/package-$1-$CEF_VERSION"

if [ ! -d "$OUTPUT" ]; then
    mkdir "$OUTPUT"
fi

CEFFRAMEWORK_DIR="$(find ${CEFBINARIES} -name "Release")/cefclient.app/Contents/Frameworks/"

echo "Copying Chromium Embedded Framework..."
rsync -a --delete "${CEFFRAMEWORK_DIR}" "${OUTPUT}/CEF/"
