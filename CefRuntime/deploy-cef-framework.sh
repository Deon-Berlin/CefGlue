#!/usr/bin/env bash
# deploy-cef-framework.sh
# Copies the CEF Framework and helper apps from a cefclient sample into an app bundle,
# renames all helper apps and their executables to match the target app name,
# patches each helper's Info.plist, and signs everything inner-to-outer.
#
# Usage:
#   deploy-cef-framework.sh <SRC_FRAMEWORKS> <DST_FRAMEWORKS> <APP_NAME> \
#                            <BUNDLE_ID> <CODESIGN_KEY> <ENTITLEMENTS> [APP_ICNS]
#
# Arguments:
#   SRC_FRAMEWORKS  Path to the source cefclient.app/Contents/Frameworks directory
#   DST_FRAMEWORKS  Path to the destination app bundle's Contents/Frameworks directory
#   APP_NAME        CFBundleName of the host app (e.g. CefGlue.Demo.Avalonia)
#   BUNDLE_ID       CFBundleIdentifier of the host app (e.g. com.example)
#   CODESIGN_KEY    Codesign identity: "-" for ad-hoc, or a Team ID / cert name
#   ENTITLEMENTS    Path to .entitlements plist for hardened-runtime signing
#   APP_ICNS        (optional) Path to an .icns file used as the Alerts helper bundle icon

set -euo pipefail

SRC="$1"
DST="$2"
APP_NAME="$3"
BUNDLE_ID="$4"
CODESIGN_KEY="${5:-}"
ENTITLEMENTS="$6"
APP_ICNS="${7:-}"

PLIST=/usr/libexec/PlistBuddy

# ---------------------------------------------------------------------------
# 1. Copy the CEF Framework verbatim
# ---------------------------------------------------------------------------
echo "Copying Chromium Embedded Framework..."
rsync -a --delete \
  "${SRC}/Chromium Embedded Framework.framework/" \
  "${DST}/Chromium Embedded Framework.framework/"

# ---------------------------------------------------------------------------
# 2. Copy, rename, and patch each helper app
#    Variants: "" (main), (Renderer), (GPU), (Plugin), (Alerts)
#    GPU intentionally shares the same bundle-id suffix as the main helper,
#    matching the cefclient convention.
#    Not every variant ships in every CEF version — e.g. CEF 148 removed the
#    "(Plugin)" helper — so any variant whose source app is absent is skipped.
# ---------------------------------------------------------------------------
get_id_suffix() {
  local v="$1"
  case "$v" in
    "")           echo ".helper" ;;
    " (Renderer)") echo ".helper.renderer" ;;
    " (GPU)")      echo ".helper" ;;
    " (Plugin)")   echo ".helper.plugin" ;;
    " (Alerts)")   echo ".helper.alerts" ;;
  esac
}

for VARIANT in "" " (Renderer)" " (GPU)" " (Plugin)" " (Alerts)"; do
  SRC_APP="${SRC}/cefclient Helper${VARIANT}.app"
  DST_APP="${DST}/${APP_NAME} Helper${VARIANT}.app"
  NEW_EXE="${APP_NAME} Helper${VARIANT}"
  SUFFIX="$(get_id_suffix "${VARIANT}")"

  if [ ! -d "${SRC_APP}" ]; then
    echo "Helper not present in this CEF version, skipping: cefclient Helper${VARIANT}.app"
    continue
  fi

  echo "Deploying helper: ${NEW_EXE}"
  rsync -a --delete "${SRC_APP}/" "${DST_APP}/"

  OLD_EXE="${DST_APP}/Contents/MacOS/cefclient Helper${VARIANT}"
  if [ -f "${OLD_EXE}" ]; then
    mv "${OLD_EXE}" "${DST_APP}/Contents/MacOS/${NEW_EXE}"
  fi
  chmod +x "${DST_APP}/Contents/MacOS/${NEW_EXE}"

  PLIST_FILE="${DST_APP}/Contents/Info.plist"
  "${PLIST}" -c "Set :CFBundleName \"${NEW_EXE}\""           "${PLIST_FILE}"
  "${PLIST}" -c "Set :CFBundleDisplayName \"${NEW_EXE}\""    "${PLIST_FILE}"
  "${PLIST}" -c "Set :CFBundleExecutable \"${NEW_EXE}\""     "${PLIST_FILE}"
  "${PLIST}" -c "Set :CFBundleIdentifier \"${BUNDLE_ID}${SUFFIX}\"" "${PLIST_FILE}"
  
  for DT_KEY in DTCompiler DTSDKBuild DTSDKName DTXcode DTXcodeBuild; do
    "${PLIST}" -c "Delete :${DT_KEY}" "${PLIST_FILE}" 2>/dev/null || true
  done

  if [ "${VARIANT}" = " (Alerts)" ] && [ -n "${APP_ICNS}" ]; then
    RESOURCES_DIR="${DST_APP}/Contents/Resources"
    mkdir -p "${RESOURCES_DIR}"
    cp "${APP_ICNS}" "${RESOURCES_DIR}/app.icns"
    "${PLIST}" -c "Delete :CFBundleIconFile" "${PLIST_FILE}" 2>/dev/null || true
    "${PLIST}" -c "Add :CFBundleIconFile string app.icns" "${PLIST_FILE}"
  fi
done

# ---------------------------------------------------------------------------
# 3. Sign everything inner-to-outer
#    Order: framework dylibs → framework → helper executables → helper apps
# ---------------------------------------------------------------------------
echo "Signing CEF Framework libraries..."
find "${DST}/Chromium Embedded Framework.framework/Libraries" -name '*.dylib' \
  -exec codesign --force --timestamp --options=runtime --sign "${CODESIGN_KEY}" {} \;

echo "Signing Chromium Embedded Framework..."
codesign --force --timestamp --options=runtime --sign "${CODESIGN_KEY}" "${DST}/Chromium Embedded Framework.framework/Chromium Embedded Framework"
codesign --force --timestamp --options=runtime --sign "${CODESIGN_KEY}" "${DST}/Chromium Embedded Framework.framework"

for VARIANT in "" " (Renderer)" " (GPU)" " (Plugin)" " (Alerts)"; do
  APP="${DST}/${APP_NAME} Helper${VARIANT}.app"
  EXE="${APP}/Contents/MacOS/${APP_NAME} Helper${VARIANT}"

  if [ ! -d "${APP}" ]; then
    continue  # variant not present in this CEF version (see step 2)
  fi

  echo "Signing helper: ${APP_NAME} Helper${VARIANT}"

  codesign --force --timestamp --options=runtime --entitlements "${ENTITLEMENTS}" --sign "${CODESIGN_KEY}" "${EXE}"
  codesign --force --timestamp --options=runtime --entitlements "${ENTITLEMENTS}" --sign "${CODESIGN_KEY}" "${APP}"
done

echo "CEF Framework deployment complete."
