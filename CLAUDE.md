# CLAUDE.md

Guidance for working in this repository. The full, authoritative CEF-upgrade
procedure lives in **[UPGRADE.md](UPGRADE.md)** — this file is orientation plus
the non-obvious gotchas that trip up an upgrade.

## What this repo is

A fork of [OutSystems/CefGlue](https://github.com/OutSystems/CefGlue) — a .NET
binding for the Chromium Embedded Framework (CEF), providing **Avalonia** and
**WPF** browser controls. Requires the **.NET 10 SDK**.

Layout is **flat**: the repo root directly contains the projects
(`CefGlue/`, `CefGlue.Common/`, `CefGlue.Avalonia/`, `CefGlue.WPF/`,
`CefGlue.Interop.Gen/`, `CefRuntime/`, `CefGlue.Tests/`, …) and the solution
`Xilium.CefGlue.slnx`. There is **no nested `CefGlue/CefGlue/` solution folder**.

The `.slnx` has **no `x64` solution configuration** — build/restore/test from
the repo root and do **not** pass `-p:Platform=x64` at the solution level
(project-level platform is fine).

## Build & test

```bash
dotnet restore Xilium.CefGlue.slnx
dotnet build   Xilium.CefGlue.slnx -c Release
dotnet test    CefGlue.Tests/CefGlue.Tests.csproj -c Release
```

- **`CefGlue` (core), `CefGlue.Common.Shared`, `CefGlue.BrowserProcess.Core`
  build without the redist packages.** Everything downstream of `CefGlue.Common`
  (Avalonia/WPF/Demos/Tests) can only *restore* once the `cef.runtime.*` packages
  for the current version exist (see below).
- **Tests: all 129 pass** as of CEF 150.0.11. `TestBase` sets
  `CefSettings.BrowserSubprocessPath` (CEF can't self-host `testhost.exe`) and a
  valid `RootCachePath` (the Chrome runtime crashes if a request-context
  `CachePath` isn't a child of `RootCachePath`) — keep both.

## Upgrading CEF — the gotchas

Do the full procedure in [UPGRADE.md](UPGRADE.md). The parts that bite:

### 1. Check the official Windows nuget package FIRST — it gates the whole upgrade

`Directory.Packages.props` pins `chromiumembeddedframework.runtime[.win-x64/.win-arm64]`
at `$(CefVersion)`, and `CefGlue.Packages.props` references them
**unconditionally**, so a `cef_version` with no published Windows package fails
`restore` with **NU1102 on every platform**, not just Windows.

```bash
curl -s https://api.nuget.org/v3-flatcontainer/chromiumembeddedframework.runtime/index.json
```

That package (same maintainer as CefSharp) **lags CEF stable by days and is
unreliable** — and **only some CEF patches per major get published** (e.g. 146
got .7 and .10; 147 and 148 got a single patch each). So do **not** wait for one
specific CEF patch — adopt whichever version actually lands, then confirm the
matching headers exist on the CDN (`https://cef-builds.spotifycdn.com/index.json`).

### 2. Version bookkeeping — two families, set by hand

| File | Property | Scheme | Example (150) |
|------|----------|--------|---------------|
| `cef-version.json` | `cefglue_version` (managed `CefGlue.Next.*`) | `{CefMajor}.{ChromeBuild}.{ChromePatch}` | `150.7871.115` |
| `CefVersion.props` | `CefRuntimePackageVersion` (redist `cef.runtime.*`) | the CEF version `{CefMajor}.{CefMinor}.{CefPatch}` | `150.0.11` |

`upgrade-cef.ps1` derives and writes `cef-version.json`; **`CefRuntimePackageVersion`
is NOT auto-derived — bump it by hand.** A fresh upgrade is a base release. To
**republish the same CEF binaries** with a build-script fix (nuget versions are
immutable), use the **x10 patch scheme**: multiply the last component by 10 and add
the patch number — managed `150.7871.115 → 150.7871.1151`, redist `150.0.11 → 150.0.111`.

### 3. Headers — overlay both platforms; don't chase enums

- Download the **`linux64` AND `windows64` minimal** packages and overlay **both**
  into `CefGlue.Interop.Gen/include/` (do not mirror-delete). The Windows-specific
  headers (`cef_sandbox_win.h`, `internal/cef_*_win.h`, `wrapper/cef_library_loader.h`)
  ship **only** in the windows package. `CefGlue.Interop.Gen/.gitignore` curates
  which headers are tracked (excludes `base/`, `capi/`, `test/`, `views/`).
- The enums in `CefGlue/Enums/*.cs` are **hand-written and deliberately partial**
  (they've lagged the header since CEF 134). New CEF values that are appends
  before a `*_NUM_VALUES` sentinel or renames **don't shift existing ordinals**, so
  binary layout is safe — **do not add them during an upgrade** unless something
  references them.

### 4. Regenerate interop; fix the generator, never the `.g.cs`

```bash
cd CefGlue.Interop.Gen
python -B cefglue_interop_gen.py --cpp-header-dir include --cefglue-dir ../CefGlue/ --no-backup
```

Generated files (`CefGlue/Classes.g/*.g.cs`, `CefGlue/Interop/**/*.g.cs`,
`version.g.cs`) say *DO NOT MODIFY*. A clean patch upgrade usually changes **only
`version.g.cs`** (version constants + API hashes). If generated output is wrong,
fix **`CefGlue.Interop.Gen/make_interop.py`** and regenerate — do not hand-edit the
`.g.cs`. (The generator emits CRLF; see line endings below.)

### 5. Redist packages (`cef.runtime.*`) — fork-custom, built by CI or locally

The Linux/macOS runtimes are **not on nuget** as official packages; this fork
builds them. Normally CI (`.github/workflows/build-cef-packages.yml`) produces
them. To build locally on Windows (uses **WSL**):

```bash
# from CefRuntime/ — run each RID; use PowerShell so /p: isn't mangled by MSYS
dotnet pack CefRuntime.csproj --runtime linux-x64   "/p:CefBuildVersion=<full+version>" -c Release
dotnet pack CefRuntime.csproj --runtime linux-arm64 "/p:CefBuildVersion=<full+version>" -c Release
dotnet pack CefRuntime.csproj --runtime osx-x64     "/p:CefBuildVersion=<full+version>" -c Release
dotnet pack CefRuntime.csproj --runtime osx-arm64   "/p:CefBuildVersion=<full+version>" -c Release
```

- Output → `LocalPackages/` (a nuget source in `Nuget.config`). **Verify size:
  ~130–145 MB good, <1 MB broken.**
- WSL needs `curl tar strip bzip2 rsync`; **`linux-arm64` also needs
  `binutils-aarch64-linux-gnu`** (`aarch64-linux-gnu-strip`).
- The pack via WSL rewrites `make_cefredist_*.sh` line endings (CRLF→LF); revert
  that noise with `git checkout -- CefRuntime/make_cefredist_*.sh` afterward.
- After rebuilding a package, clear its stale extraction under `packages/cef.runtime.<rid>/<ver>`
  before re-restoring, or nuget reuses the old copy.

## Conventions

- **Line endings:** tracked `.cs`/`.py` files are **LF** in HEAD. Some editors flip
  them to CRLF and produce whole-file diffs. Verify with the git blob
  (`b"\r\n" in $(git cat-file -p HEAD:<file>)`), not `grep -cU $'\r'` (unreliable
  here). When an edit churns the whole file, normalize back to the file's original
  EOL (a byte-level replace preserving the existing ending works cleanly).
- **CEF proxy lifetime:** proxies are ref-counted (`AddRef`/`Release` on the native
  object). A proxy's `ToNative()` must `AddRef()` before handing the pointer to a
  native "in" parameter — CEF's `CToCpp::Wrap` consumes one reference. Skipping it
  frees the object under a live managed proxy (use-after-free crash).
- Commits follow **Conventional Commits** (`fix(...)`, `build(...)`, `test:` …).
