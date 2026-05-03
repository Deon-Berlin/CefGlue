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

# ── Step 3: Update GitHub Actions workflow ────────────────────────────────────
WORKFLOW_FILE="${SCRIPT_DIR}/.github/workflows/build-cef-packages.yml"

if [ -f "$WORKFLOW_FILE" ]; then
    step "Updating .github/workflows/build-cef-packages.yml"

    # Use Python to safely replace the default cefbuildversion value.
    # The sed approach is unreliable because the version string contains '+'.
    python3 - "$WORKFLOW_FILE" "$CEF_BUILD_VERSION" <<'PYEOF'
import sys
import re

workflow_path = sys.argv[1]
new_version   = sys.argv[2]

with open(workflow_path, 'r') as f:
    content = f.read()

# Match the default: "..." line that holds a CEF build version string.
# Pattern is specific: the current value contains +g<hash>+chromium-
updated, n = re.subn(
    r'(default:\s*")[^"]*\+g[0-9a-f]+\+chromium-[^"]*(")',
    r'\g<1>' + new_version + r'\2',
    content
)

if n == 0:
    print("WARNING: Could not find cefbuildversion default value in workflow file.", file=sys.stderr)
    sys.exit(1)

with open(workflow_path, 'w') as f:
    f.write(updated)
PYEOF

    ok ".github/workflows/build-cef-packages.yml updated"
else
    warn ".github/workflows/build-cef-packages.yml not found — skipping"
fi

# ── Step 5: Download CEF C API headers ───────────────────────────────────────
if [ "$SKIP_DOWNLOAD" = false ]; then
    step "Downloading CEF C API headers"

    ENCODED_VERSION=$(python3 -c "import sys; print(sys.argv[1].replace('+', '%2B'))" "$CEF_BUILD_VERSION")
    DOWNLOAD_URL="https://cef-builds.spotifycdn.com/cef_binary_${ENCODED_VERSION}_linux64_minimal.tar.bz2"
    TEMP_DIR="${SCRIPT_DIR}/.upgrade-cef"
    mkdir -p "${TEMP_DIR}"

    info "URL: ${DOWNLOAD_URL}"
    curl -L --fail --progress-bar -o "${TEMP_DIR}/cef.tar.bz2" "$DOWNLOAD_URL"

    step "Extracting headers"
    mkdir -p "${TEMP_DIR}/cef_extract"
    tar -jxf "${TEMP_DIR}/cef.tar.bz2" -C "${TEMP_DIR}/cef_extract"

    INCLUDE_DEST="${SCRIPT_DIR}/CefGlue/CefGlue.Interop.Gen/include"
    mkdir -p "$INCLUDE_DEST"
    cp -r "${TEMP_DIR}/cef_extract/"*/include/* "$INCLUDE_DEST/"

    rm -rf "$TEMP_DIR"
    ok "CEF headers installed to CefGlue/CefGlue.Interop.Gen/include/"
else
    warn "Skipping header download (--skip-download)"
fi

# ── Step 6: Regenerate interop bindings ───────────────────────────────────────
if [ "$SKIP_INTEROP" = false ]; then
    step "Regenerating interop bindings"

    INTEROP_DIR="${SCRIPT_DIR}/CefGlue/CefGlue.Interop.Gen"
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
    (cd "${SCRIPT_DIR}/CefGlue" && dotnet build Xilium.CefGlue.slnx -c Release -p:Platform=x64)
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
echo "  ✓ .github/workflows/build-cef-packages.yml updated"
[ "$SKIP_DOWNLOAD" = false ]  && echo "  ✓ CEF C API headers downloaded"
[ "$SKIP_INTEROP" = false ]   && echo "  ✓ Interop bindings regenerated"
[ "$DO_BUILD" = true ]        && echo "  ✓ Solution built"
echo ""
echo -e "${YELLOW}Manual steps still required:${NC}"
echo "  1. Fix any API breaking changes in CefGlue source code"
echo "     → cd CefGlue && dotnet build Xilium.CefGlue.slnx -c Release -p:Platform=x64"
echo "  2. Build CEF redistribution packages:"
echo "     → ./build-local-packages.ps1"
echo "     → or: cd runtime-packages && dotnet pack runtime-packages.csproj --runtime linux-x64 /p:CefBuildVersion=${CEF_BUILD_VERSION}"
echo "  3. Run tests:"
echo "     → cd CefGlue && dotnet test CefGlue.Tests/CefGlue.Tests.csproj -c Release -p:Platform=x64"
echo "  4. Update README.md with new version information"
echo "  5. Commit changes:"
echo "     → git add -A && git commit -m 'Upgrade CEF to ${CEF_VERSION}'"
echo ""
