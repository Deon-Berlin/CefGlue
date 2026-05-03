# CEF Version Upgrade Guide

This document describes the complete process for upgrading the CEF (Chromium Embedded Framework) version used in this repository.

## Quick Upgrade (Automated)

Use the provided upgrade scripts to automate Steps 2–6 of the manual process below.

**Windows (PowerShell):**
```powershell
.\upgrade-cef.ps1 144.0.13+g9f739aa+chromium-144.0.7559.133
```

**Linux / macOS (bash):**
```bash
./upgrade-cef.sh 144.0.13+g9f739aa+chromium-144.0.7559.133
```

The scripts will:
- Parse the version string into its components
- Update `cef-version.json`
- Update the default version in `.github/workflows/build-cef-packages.yml`
- Download the CEF C API headers from cef-builds.spotifycdn.com
- Regenerate the interop bindings via `cefglue_interop_gen.py`

After the scripts complete, continue with the **manual steps** below:
- [Step 5 — Handle API Breaking Changes](#step-5-handle-api-breaking-changes)
- [Step 6 — Build CEF Redistribution Packages](#step-6-build-cef-redistribution-packages)
- [Step 7 — Clean Up Old Build Artifacts](#step-7-clean-up-old-build-artifacts)
- [Step 8 — Build and Test the Full Solution](#step-8-build-and-test-the-full-solution)
- [Step 9 — Update Documentation](#step-9-update-documentation)

### Script Options

| Option | PowerShell | Bash | Effect |
|--------|-----------|------|--------|
| Skip header download | `-SkipDownload` | `--skip-download` | Don't download CEF C API headers |
| Skip interop regen | `-SkipInterop` | `--skip-interop` | Don't regenerate interop bindings |
| Build after update | `-Build` | `--build` | Run `dotnet build` at the end |

---

## Prerequisites

- .NET 8.0 SDK
- Git
- For building CEF redistribution packages:
  - `curl` and `tar` with bzip2 support
  - `strip` (binutils) — for stripping debug symbols from Linux binaries
  - `nuget.exe` (or Mono on Linux/macOS)
- For running the interop generator:
  - Python 3.x

## Version Configuration

All CEF version information is centralized in a single file:

**[cef-version.json](cef-version.json)**

```json
{
    "cef_version": "144.0.13",
    "cef_build_version": "144.0.13+g9f739aa+chromium-144.0.7559.133",
    "chromium_version": "144.0.7559.133",
    "cefglue_version": "144.7559.133",
    "cef_git_hash": "g9f739aa"
}
```

This file is consumed by:

| Consumer | How it reads the version |
|----------|------------------------|
| `CefGlue/Directory.Build.props` | Via `CefVersion.props` MSBuild import (regex parsing) |
| `build-local-packages.ps1` | PowerShell `ConvertFrom-Json` |
| `runtime-packages/make_cefredist_linux.sh` | `grep` + `sed` |
| `runtime-packages/make_cefredist_osx.sh` | `grep` + `sed` |
| `.github/workflows/build-cef-packages.yml` | `cefbuildversion` workflow input (set when triggering manually) |

## Step-by-Step Upgrade Process (Manual / Full Reference)

> **Note:** Steps 2–4 are automated by `upgrade-cef.ps1` / `upgrade-cef.sh` (see [Quick Upgrade](#quick-upgrade-automated) above).

### Step 1: Determine the New CEF Version

1. Visit [https://cef-builds.spotifycdn.com/index.html](https://cef-builds.spotifycdn.com/index.html)
2. Find the desired CEF release (e.g., "Current Stable Build"). Be sure there is a package with the same version available for Windows [https://www.nuget.org/packages/chromiumembeddedframework.runtime](https://www.nuget.org/packages/chromiumembeddedframework.runtime).
3. Note the full build version string, e.g.:
   ```
   144.0.13+g9f739aa+chromium-144.0.7559.133
   ```
4. Extract the components:
   - **CEF Version**: `144.0.13`
   - **Git Hash**: `g9f739aa`
   - **Chromium Version**: `144.0.7559.133`
   - **CefGlue Version**: `{CEF_MAJOR}.{CHROME_BUILD}.{CHROME_PATCH}` → `144.7559.133`

### Step 2: Update `cef-version.json`

Edit the file at the repository root:

```json
{
    "cef_version": "<NEW_CEF_VERSION>",
    "cef_build_version": "<FULL_BUILD_STRING>",
    "chromium_version": "<CHROMIUM_VERSION>",
    "cefglue_version": "<CEFGLUE_VERSION>",
    "cef_git_hash": "<GIT_HASH>"
}
```

This single change propagates to:
- `CefGlue/Directory.Build.props` (via `CefVersion.props` import)
- `build-local-packages.ps1`
- `runtime-packages/make_cefredist_linux.sh`
- `runtime-packages/make_cefredist_osx.sh`

### Step 3: Download New CEF C API Headers

The CefGlue interop layer is generated from CEF's C API headers. Download the new headers:

1. Go to [https://cef-builds.spotifycdn.com/index.html](https://cef-builds.spotifycdn.com/index.html)
2. Download the **minimal** distribution for any platform (the headers are the same across platforms)
3. Extract the archive
4. Copy the `include/` directory contents to `CefGlue/CefGlue.Interop.Gen/include/`

```bash
# Example for Linux x64
CEF_BUILD_VERSION="144.0.13+g9f739aa+chromium-144.0.7559.133"
ENCODED_VERSION=$(echo "$CEF_BUILD_VERSION" | sed 's/+/%2B/g')
curl -o cef.tar.bz2 "https://cef-builds.spotifycdn.com/cef_binary_${ENCODED_VERSION}_linux64_minimal.tar.bz2"
mkdir -p cef_extract && tar -jxf cef.tar.bz2 -C cef_extract
cp -r cef_extract/*/include/* CefGlue/CefGlue.Interop.Gen/include/
```

### Step 4: Regenerate Interop Bindings

Run the interop generator to update the C# bindings from the new headers:

```bash
cd CefGlue/CefGlue.Interop.Gen
python3 -B cefglue_interop_gen.py --cpp-header-dir include --cefglue-dir ../CefGlue/ --no-backup
```

This updates:
- `CefGlue/CefGlue/Interop/version.g.cs` — CEF version constants and API hashes
- `CefGlue/CefGlue/Classes.g/` — Auto-generated CEF class wrappers
- `CefGlue/CefGlue/Enums/` — CEF enum definitions (if changed)
- `CefGlue/CefGlue/Structs/` — CEF struct definitions (if changed)

### Step 5: Handle API Breaking Changes

After regenerating interop bindings, the build may fail due to CEF API changes:

1. **Build the solution** to identify compilation errors:
   ```bash
   cd CefGlue
   dotnet build Xilium.CefGlue.slnx -c Release -p:Platform=x64
   ```

2. **Review CEF release notes** for breaking changes:
   - Check the [CEF changelog](https://bitbucket.org/nickhutchinson/chromiumembedded/wiki/BranchesAndBuilding)
   - Review removed/changed/added APIs

3. **Fix compilation errors** in:
   - `CefGlue/CefGlue/Classes.Handlers/` — Handler implementations
   - `CefGlue/CefGlue/Classes.Proxies/` — Proxy class implementations
   - `CefGlue/CefGlue/Wrapper/` — Wrapper classes
   - `CefGlue/CefGlue.Common/` — Common browser adapter code
   - `CefGlue/CefGlue.Avalonia/` — Avalonia-specific code
   - `CefGlue/CefGlue.WPF/` — WPF-specific code

### Step 6: Build CEF Redistribution Packages

Build the platform-specific CEF binary NuGet packages:

#### Option A: Using the GitHub Actions workflow (recommended)

Trigger the workflow manually from the Actions tab, providing the full CEF build version string as the `cefbuildversion` input. The workflow builds packages for all platforms (Linux x64, Linux ARM64, macOS x64, macOS ARM64) and uploads them as artifacts.

#### Option B: Building locally

```bash
# Linux x64
cd runtime-packages
dotnet pack runtime-packages.csproj --runtime linux-x64 /p:CefBuildVersion=<FULL_BUILD_STRING>

# Linux ARM64
dotnet pack runtime-packages.csproj --runtime linux-arm64 /p:CefBuildVersion=<FULL_BUILD_STRING>

# macOS x64
dotnet pack runtime-packages.csproj --runtime osx-x64 /p:CefBuildVersion=<FULL_BUILD_STRING>

# macOS ARM64
dotnet pack runtime-packages.csproj --runtime osx-arm64 /p:CefBuildVersion=<FULL_BUILD_STRING>
```

Packages are output to the `LocalPackages/` directory, which is configured as a NuGet source in `CefGlue/Nuget.config`.

### Step 7: Clean Up Old Build Artifacts

Remove any stale NuGet packages from a previous version:

```bash
rm -f LocalPackages/cef.runtime.*.nupkg
```

### Step 8: Build and Test the Full Solution

```bash
cd CefGlue

# Restore packages (picks up new CEF redist packages from LocalPackages)
dotnet restore Xilium.CefGlue.slnx -p:Platform=x64

# Build
dotnet build Xilium.CefGlue.slnx -c Release -p:Platform=x64

# Run tests
dotnet test CefGlue.Tests/CefGlue.Tests.csproj -c Release -p:Platform=x64

# Run demo to verify runtime behavior
dotnet run --project CefGlue.Demo.Avalonia -c Release -p:Platform=x64
```

### Step 9: Update Documentation

1. Update `README.md`:
   - Title and description with new CEF version
   - Version table
   - Any new platform support or known issues

2. Commit all changes:
   ```bash
   git add -A
   git commit -m "Upgrade CEF to <NEW_VERSION>"
   ```

## Version Mapping Reference

The version numbers follow this pattern:

| Component | Format | Example |
|-----------|--------|---------|
| CEF Version | `MAJOR.MINOR.PATCH` | `144.0.13` |
| CEF Build Version | `CEF_VERSION+gHASH+chromium-CHROME_VERSION` | `144.0.13+g9f739aa+chromium-144.0.7559.133` |
| Chromium Version | `MAJOR.MINOR.BUILD.PATCH` | `144.0.7559.133` |
| CefGlue Version | `CEF_MAJOR.CHROME_BUILD.CHROME_PATCH` | `144.7559.133` |

## Files Modified During an Upgrade

| File | What changes | Auto/Manual |
|------|-------------|-------------|
| `cef-version.json` | All version numbers | **Manual** (single source of truth) |
| `.github/workflows/build-cef-packages.yml` | `cefbuildversion` workflow input default | **Manual** (set at trigger time or update default) |
| `CefGlue/Directory.Build.props` | Version properties | **Auto** (reads from `cef-version.json` via `CefVersion.props`) |
| `runtime-packages/make_cefredist_linux.sh` | Download URL | **Auto** (reads from `cef-version.json`) |
| `runtime-packages/make_cefredist_osx.sh` | Download URL | **Auto** (reads from `cef-version.json`) |
| `build-local-packages.ps1` | `$CefVersion` variable | **Auto** (reads from `cef-version.json`) |
| `CefGlue/CefGlue/Interop/version.g.cs` | CEF version constants, API hashes | **Auto** (regenerated by `cefglue_interop_gen.py`) |
| `CefGlue/CefGlue.Interop.Gen/include/` | CEF C API headers | **Manual** (download from CEF builds) |
| `CefGlue/CefGlue/Classes.g/` | Generated interop classes | **Auto** (regenerated by `cefglue_interop_gen.py`) |
| `README.md` | Version references, release notes | **Manual** |

## Troubleshooting

### Build fails after interop regeneration

CEF may have added, removed, or changed API methods. Check:
- New abstract methods on handler classes that need implementation
- Changed method signatures in proxy classes
- New enum values or removed constants

### NuGet package not found during restore

Ensure:
1. `LocalPackages/` contains the `.nupkg` files for the new version
2. `CefGlue/Nuget.config` lists `LocalPackages` as a source
3. Run `dotnet nuget locals all --clear` to clear the NuGet cache

### CEF binary download fails

1. Verify the build version string is correct on [cef-builds.spotifycdn.com](https://cef-builds.spotifycdn.com/index.html)
2. Check that `+` characters are properly URL-encoded as `%2B` in download URLs
3. Try downloading manually to verify the URL works

### API hash mismatch at runtime

If you get an error about CEF API hash mismatch:
1. Ensure `version.g.cs` was regenerated with the correct CEF headers
2. Verify the CEF binaries match the version in `cef-version.json`
3. Clean and rebuild: `dotnet clean && dotnet build`

### Linux ARM64 issues

See [CefGlue/LINUX.md](CefGlue/LINUX.md) for ARM64-specific workarounds related to TLS (Thread Local Storage) limitations.
