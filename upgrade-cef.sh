#!/usr/bin/env bash
# CEF Version Upgrade Script for Linux/macOS
#
# Usage:
#   ./upgrade-cef.sh <cef-build-version> [options]
#
# Example:
#   ./upgrade-cef.sh 144.0.13+g9f739aa+chromium-144.0.7559.133
#
# Options:
#   --skip-download    Skip downloading CEF C API headers
#   --skip-interop     Skip regenerating interop bindings
#   --build            Build the solution after updating
#   --help             Show this help message

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# ── Colours ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

step()  { echo -e "\n${BLUE}${BOLD}==>${NC} $1"; }
ok()    { echo -e "  ${GREEN}✓${NC} $1"; }
warn()  { echo -e "  ${YELLOW}!${NC} $1"; }
err()   { echo -e "  ${RED}✗${NC} $1" >&2; }
info()  { echo -e "  ${CYAN}·${NC} $1"; }

# ── Usage ─────────────────────────────────────────────────────────────────────
usage() {
    echo ""
    echo "  Usage: $0 <cef-build-version> [options]"
    echo ""
    echo "  Arguments:"
    echo "    <cef-build-version>   Full CEF build version string"
    echo "                          e.g. 144.0.13+g9f739aa+chromium-144.0.7559.133"
    echo ""
    echo "  Options:"
    echo "    --skip-download    Skip downloading CEF C API headers"
    echo "    --skip-interop     Skip regenerating interop bindings"
    echo "    --build            Build the solution after updating"
    echo "    --help             Show this help message"
    echo ""
    exit 1
}

# ── Argument parsing ──────────────────────────────────────────────────────────
SKIP_DOWNLOAD=false
SKIP_INTEROP=false
DO_BUILD=false
CEF_BUILD_VERSION=""

for arg in "$@"; do
    case "$arg" in
        --skip-download) SKIP_DOWNLOAD=true ;;
        --skip-interop)  SKIP_INTEROP=true ;;
        --build)         DO_BUILD=true ;;
        --help|-h)       usage ;;
        -*)
            err "Unknown option: $arg"
            usage
            ;;
        *)
            if [ -z "$CEF_BUILD_VERSION" ]; then
                CEF_BUILD_VERSION="$arg"
            else
                err "Unexpected argument: $arg"
                usage
            fi
            ;;
    esac
done

if [ -z "$CEF_BUILD_VERSION" ]; then
    err "Missing required argument: <cef-build-version>"
    usage
fi

# ── Version string validation ─────────────────────────────────────────────────
if ! echo "$CEF_BUILD_VERSION" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+\+g[0-9a-f]+\+chromium-[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$'; then
    err "Invalid CEF build version format: $CEF_BUILD_VERSION"
    err "Expected: MAJOR.MINOR.PATCH+gHASH+chromium-MAJOR.MINOR.BUILD.PATCH"
    exit 1
fi

# ── Parse version components ──────────────────────────────────────────────────
# CEF version: 144.0.13
CEF_VERSION="${CEF_BUILD_VERSION%%+*}"

# Git hash: g9f739aa
_AFTER_CEF="${CEF_BUILD_VERSION#*+}"
CEF_GIT_HASH="${_AFTER_CEF%%+*}"

# Chromium version: 144.0.7559.133
CHROMIUM_VERSION="${CEF_BUILD_VERSION##*+chromium-}"

# CefGlue version: CEF_MAJOR.CHROME_BUILD.CHROME_PATCH → 144.7559.133
CEF_MAJOR="${CEF_VERSION%%.*}"
IFS='.' read -r -a _CHROME_PARTS <<< "$CHROMIUM_VERSION"
CHROME_BUILD="${_CHROME_PARTS[2]}"
CHROME_PATCH="${_CHROME_PARTS[3]}"
CEFGLUE_VERSION="${CEF_MAJOR}.${CHROME_BUILD}.${CHROME_PATCH}"

# ── Banner ────────────────────────────────────────────────────────────────────
echo ""
echo -e "${BOLD}CEF Version Upgrade${NC}"
echo "────────────────────────────────────────"
info "Build version:    ${CEF_BUILD_VERSION}"
info "CEF version:      ${CEF_VERSION}"
info "Git hash:         ${CEF_GIT_HASH}"
info "Chromium version: ${CHROMIUM_VERSION}"
info "CefGlue version:  ${CEFGLUE_VERSION}"
echo ""

# ── Step 2: Update cef-version.json ──────────────────────────────────────────
step "Updating cef-version.json"

cat > "${SCRIPT_DIR}/cef-version.json" <<EOF
{
  "cef_version": "${CEF_VERSION}",
  "cef_build_version": "${CEF_BUILD_VERSION}",
  "chromium_version": "${CHROMIUM_VERSION}",
  "cefglue_version": "${CEFGLUE_VERSION}",
  "cef_git_hash": "${CEF_GIT_HASH}"
}
EOF

ok "cef-version.json updated"

# ── Step 3: Update the redist (cef.runtime.*) package version ───────────────
# A fresh upgrade is a base release, so the redist version is the CEF version.
# (build-cef-packages.yml reads cef-version.json itself; it needs no update.)
step "Updating CefRuntimePackageVersion in CefVersion.props"

PROPS_FILE="${SCRIPT_DIR}/CefVersion.props"
if grep -q '<CefRuntimePackageVersion>' "$PROPS_FILE"; then
    perl -pi -e "s|<CefRuntimePackageVersion>[^<]*</CefRuntimePackageVersion>|<CefRuntimePackageVersion>${CEF_VERSION}</CefRuntimePackageVersion>|" "$PROPS_FILE"
    ok "CefRuntimePackageVersion set to ${CEF_VERSION}"
else
    err "CefRuntimePackageVersion not found in CefVersion.props"
    exit 1
fi

# ── Step 4: Check the official Windows runtime package ────────────────────────
# Directory.Packages.props pins chromiumembeddedframework.runtime at $(CefVersion)
# unconditionally, so restore fails with NU1102 on every platform until it exists.
step "Checking nuget.org for chromiumembeddedframework.runtime ${CEF_VERSION}"

NUGET_INDEX_URL="https://api.nuget.org/v3-flatcontainer/chromiumembeddedframework.runtime/index.json"
if NUGET_INDEX=$(curl -sfL "$NUGET_INDEX_URL"); then
    NUGET_VERSIONS=$(echo "$NUGET_INDEX" | tr -d ' \n\r[]{}"' | sed 's/^versions://' | tr ',' '\n')
    if echo "$NUGET_VERSIONS" | grep -qx "$CEF_VERSION"; then
        ok "chromiumembeddedframework.runtime ${CEF_VERSION} is published"
    else
        warn "chromiumembeddedframework.runtime ${CEF_VERSION} is NOT published (latest: $(echo "$NUGET_VERSIONS" | tail -1))"
        warn "dotnet restore will fail with NU1102 on every platform until it is"
    fi
else
    warn "Could not reach nuget.org; skipping the Windows package check"
fi

# ── Step 5: Download CEF C API headers ───────────────────────────────────────
# Overlay linux64 then windows64 (do not mirror-delete): the Windows-specific
# headers (cef_sandbox_win.h, internal/cef_*_win.h, wrapper/cef_library_loader.h)
# ship only in the windows package.
if [ "$SKIP_DOWNLOAD" = false ]; then
    ENCODED_VERSION=$(python3 -c "import sys; print(sys.argv[1].replace('+', '%2B'))" "$CEF_BUILD_VERSION")
    TEMP_DIR="${SCRIPT_DIR}/.upgrade-cef"
    INCLUDE_DEST="${SCRIPT_DIR}/CefGlue.Interop.Gen/include"
    rm -rf "$TEMP_DIR"
    mkdir -p "$TEMP_DIR" "$INCLUDE_DEST"
    trap 'rm -rf "$TEMP_DIR"' EXIT

    for PLATFORM in linux64 windows64; do
        step "Downloading CEF C API headers (${PLATFORM})"
        DOWNLOAD_URL="https://cef-builds.spotifycdn.com/cef_binary_${ENCODED_VERSION}_${PLATFORM}_minimal.tar.bz2"
        info "URL: ${DOWNLOAD_URL}"
        curl -L --fail --progress-bar -o "${TEMP_DIR}/${PLATFORM}.tar.bz2" "$DOWNLOAD_URL"

        mkdir -p "${TEMP_DIR}/${PLATFORM}"
        tar -jxf "${TEMP_DIR}/${PLATFORM}.tar.bz2" -C "${TEMP_DIR}/${PLATFORM}"
        cp -R "${TEMP_DIR}/${PLATFORM}/"*/include/* "$INCLUDE_DEST/"
        ok "${PLATFORM} headers installed to CefGlue.Interop.Gen/include/"
    done

    rm -rf "$TEMP_DIR"
    trap - EXIT
else
    warn "Skipping header download (--skip-download)"
fi

# ── Step 6: Regenerate interop bindings ───────────────────────────────────────
if [ "$SKIP_INTEROP" = false ]; then
    step "Regenerating interop bindings"

    INTEROP_DIR="${SCRIPT_DIR}/CefGlue.Interop.Gen"
    if [ -f "${INTEROP_DIR}/cefglue_interop_gen.py" ]; then
        (
            cd "$INTEROP_DIR"
            python3 -B cefglue_interop_gen.py \
                --cpp-header-dir include \
                --cefglue-dir ../CefGlue/ \
                --no-backup
        )
        ok "Interop bindings regenerated"
    else
        warn "cefglue_interop_gen.py not found in ${INTEROP_DIR} — skipping"
    fi
else
    warn "Skipping interop regeneration (--skip-interop)"
fi

# ── Step 7/10: Build the solution ─────────────────────────────────────────────
if [ "$DO_BUILD" = true ]; then
    step "Building solution"
    (cd "${SCRIPT_DIR}" && dotnet build Xilium.CefGlue.slnx -c Release)
    ok "Solution built successfully"
fi

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo -e "${BOLD}════════════════════════════════════════${NC}"
echo -e "${BOLD} Automated upgrade steps complete${NC}"
echo -e "${BOLD}════════════════════════════════════════${NC}"
echo ""
echo -e "${GREEN}Completed:${NC}"
echo "  ✓ cef-version.json updated"
echo "  ✓ CefRuntimePackageVersion set to ${CEF_VERSION}"
[ "$SKIP_DOWNLOAD" = false ]  && echo "  ✓ CEF C API headers downloaded (linux64 + windows64)"
[ "$SKIP_INTEROP" = false ]   && echo "  ✓ Interop bindings regenerated"
[ "$DO_BUILD" = true ]        && echo "  ✓ Solution built"
echo ""
echo -e "${YELLOW}Manual steps still required:${NC}"
echo "  1. Review the generated diff: a clean upgrade usually changes only version.g.cs;"
echo "     revert whitespace-only or path-separator-only churn in other generated files"
echo "     and keep each generated file's line endings as in git (version.g.cs is CRLF)"
echo "  2. Fix any API breaking changes in CefGlue source code"
echo "     → dotnet build Xilium.CefGlue.slnx -c Release"
echo "  3. Build the CEF redistribution packages (cef.runtime.*):"
echo "     → run the build-cef-packages workflow, or locally per RID:"
echo "     → cd CefRuntime && dotnet pack CefRuntime.csproj --runtime <rid> \"/p:CefBuildVersion=${CEF_BUILD_VERSION}\" -c Release"
echo "  4. Run tests:"
echo "     → dotnet test CefGlue.Tests/CefGlue.Tests.csproj -c Release"
echo "  5. Update README.md with new version information"
echo "  6. Commit changes:"
echo "     → git add -A && git commit -m 'build(cef): upgrade to ${CEF_VERSION}'"
echo ""
