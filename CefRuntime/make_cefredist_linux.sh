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

TMP="$BASE/tmp-$1"
OUTPUT="./package-$1"

if [ "$1" == "linux64" ]; then 
    ARCH="linux64";
else
    ARCH="linuxarm64";
fi

if [ ! -d "$TMP" ]; then
    mkdir "$TMP"
fi

cd "$TMP"

rm -rf "$OUTPUT"
mkdir ".$OUTPUT"

CEFZIP="cef-${CEF_VERSION}.tar.bz2"
CEFBINARIES="cef_binaries-${CEF_VERSION}"

if [ ! -f "$CEFZIP" ]; then
    URL="https://cef-builds.spotifycdn.com/cef_binary_${CEF_BUILD_VERSION//+/%2B}_${ARCH}_minimal.tar.bz2"
    echo "downloading cef binaries"
    if ! command -v aria2c &> /dev/null
    then
        curl -o "$CEFZIP" "$URL"
    else
        aria2c -c -o "$CEFZIP" "$URL"
    fi
fi

if [ ! -d "$CEFBINARIES" ]; then
    echo "unzipping cef binaries"
    mkdir "$CEFBINARIES"
    tar -jxvf "$CEFZIP" -C "./$CEFBINARIES"
fi
echo "copying cef binaries"
cp -va "${PWD}/$(find $CEFBINARIES -name "Release")/." ".$OUTPUT/CEF/"
cd .. || exit 1
echo "stripping cef binaries"
if [ "$1" == "linux64" ]; then 
	strip -v -s "${OUTPUT}/CEF/libcef.so"
	strip -v -s "${OUTPUT}/CEF/libEGL.so"
	strip -v -s "${OUTPUT}/CEF/libGLESv2.so"
	strip -v -s "${OUTPUT}/CEF/libvk_swiftshader.so"
	strip -v -s "${OUTPUT}/CEF/libvulkan.so.1"
else
	aarch64-linux-gnu-strip -v -s "${OUTPUT}/CEF/libcef.so"
	aarch64-linux-gnu-strip -v -s "${OUTPUT}/CEF/libEGL.so"
	aarch64-linux-gnu-strip -v -s "${OUTPUT}/CEF/libGLESv2.so"
	aarch64-linux-gnu-strip -v -s "${OUTPUT}/CEF/libvk_swiftshader.so"
	aarch64-linux-gnu-strip -v -s "${OUTPUT}/CEF/libvulkan.so.1"
fi
cd "$TMP" || exit 1
cp -Rv "${PWD}/$(find $CEFBINARIES -name "Resources")/." ".$OUTPUT/CEF/Resources"
