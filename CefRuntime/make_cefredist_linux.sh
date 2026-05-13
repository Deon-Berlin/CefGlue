#!/usr/bin/env bash
set -e

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

if [ "$1" == "linux-x64" ]; then
    ARCH="linux64"
else
    ARCH="linuxarm64"
fi

if [ ! -d "$TMP" ]; then
    mkdir "$TMP"
fi

CEFZIP="${TMP}/cef-${CEF_VERSION}.tar.bz2"
CEFBINARIES="${TMP}/cef_binaries-${CEF_VERSION}"
OUTPUT="${BASE}/package-$1-${CEF_VERSION}"

if [ ! -f "$CEFZIP" ]; then
    URL="https://cef-builds.spotifycdn.com/cef_binary_${CEF_BUILD_VERSION//+/%2B}_${ARCH}_minimal.tar.bz2"
    echo "Downloading CEF binaries v${CEF_VERSION}-${ARCH}"
    if command -v aria2c &> /dev/null; then
        aria2c -c -o "$CEFZIP" "$URL"
    else
        curl -o "$CEFZIP" "$URL"
    fi
fi

if [ ! -d "$CEFBINARIES" ]; then
    echo "Extracting CEF binaries v${CEF_VERSION}-${ARCH}"
    mkdir "$CEFBINARIES"
    tar -jxvf "$CEFZIP" -C "$CEFBINARIES"
else
    echo "CEF binaries v${CEF_VERSION}-${ARCH} already extracted"
fi

RELEASE_DIR="$(find "$CEFBINARIES" -name "Release" -type d)"
if [ -z "$RELEASE_DIR" ]; then
    echo "ERROR: Release directory not found in ${CEFBINARIES}"
    exit 1
fi

rm -rf "${OUTPUT}"
mkdir -p "${OUTPUT}/CEF"

echo "Copying CEF binaries..."
cp -a "${RELEASE_DIR}/." "${OUTPUT}/CEF/"

echo "Stripping CEF binaries..."
if [ "$1" == "linux-x64" ]; then
    STRIP="strip"
else
    STRIP="aarch64-linux-gnu-strip"
fi

"$STRIP" -v -s "${OUTPUT}/CEF/libcef.so"
"$STRIP" -v -s "${OUTPUT}/CEF/libEGL.so"
"$STRIP" -v -s "${OUTPUT}/CEF/libGLESv2.so"
"$STRIP" -v -s "${OUTPUT}/CEF/libvk_swiftshader.so"
"$STRIP" -v -s "${OUTPUT}/CEF/libvulkan.so.1"

echo "Copying CEF resources..."
RESOURCES_DIR="$(find "$CEFBINARIES" -name "Resources" -type d | head -1)"
if [ -n "$RESOURCES_DIR" ]; then
    cp -Rv "${RESOURCES_DIR}/." "${OUTPUT}/CEF/Resources"
fi
