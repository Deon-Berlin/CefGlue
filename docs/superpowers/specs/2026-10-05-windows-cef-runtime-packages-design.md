# Fork-maintained Windows CEF runtime packages

Status: implemented on `build/cef-154.0.33`
Date: 2026-10-05
Branch: `build/cef-154.0.33`

## Problem

Windows CEF binaries come from `chromiumembeddedframework.runtime`,
`chromiumembeddedframework.runtime.win-x64` and `.win-arm64`, published by the CefSharp
maintainer. `Directory.Packages.props` pins them at `$(CefVersion)` and
`CefGlue.Packages.props` references them unconditionally, so a CEF version with no
published Windows package fails `restore` with **NU1102 on every platform**, not just
Windows. Every release of this fork is therefore paced by a third party.

That is not a hypothetical cost on this branch. CEF 154.0.33 is on the CDN as a stable
build for `windows32`, `windows64` and `windowsarm64`, but upstream's newest package is
152.0.10, so on `build/cef-154.0.33` today:

```
error NU1102: chromiumembeddedframework.runtime.win-x64 (>= 154.0.33) not found
  - 129 version(s) found in "Nuget" [ nearest: 152.0.10 ]
```

Nothing downstream of `CefGlue.Common` (Avalonia, WPF, demos, tests) can restore. The 154
release is blocked until the fork builds its own Windows packages.

A second, smaller problem: upstream splits the payload. The per-arch package carries
`runtimes/win-x64/native/` while the shared `chromiumembeddedframework.runtime` package
carries only `CEF/<arch>/locales/`. Consumers need both, and the current Windows copy
only works because `CefGlue.Common.targets` calls `CallTarget` into targets defined inside
the upstream packages.

## Goal

Build and maintain Windows CEF runtime packages in this fork, the same way it already does
for Linux and macOS: one self-contained package per architecture, consumed identically, so
a CEF upgrade is gated only by CDN availability.

## Decisions

| Question | Decision |
|---|---|
| Architectures | `win-x64` and `win-arm64` only. `win-x86` deferred until something needs it. |
| Project | Extend the existing `CefRuntime.csproj` (approach A), no second project. |
| Staging | A PowerShell script, `make_cefredist_windows.ps1`. No WSL. |
| Arch when no RID | Fall back to `$(Platform)`, defaulting to `win-x64`. |
| Publish | Windows packages copy on build *and* publish. Linux/macOS publish gap is a follow-up. |
| Payload | Runtime files only: drop `libcef.lib` and `bootstrap.exe`/`bootstrapc.exe`. |
| Existing Linux/macOS props | Left untouched. |

## Package design

### Identity and versioning

Two packages, `cef.runtime.win-x64` and `cef.runtime.win-arm64`, versioned from
`$(CefRuntimePackageVersion)` (154.0.33 for this release) exactly like the Linux and macOS
packages, including the x10 patch scheme for republishing the same binaries with a build
fix. They are produced by `CefRuntime.csproj` with `win-x64;win-arm64` added to
`RuntimeIdentifiers`; `PackageId`, license, icon, description and the pack plumbing are
already RID-generic and need no change.

### Staging script

`CefRuntime/make_cefredist_windows.ps1 <rid> [<cefBuildVersion>]`, mirroring the contract
of the two bash scripts:

- When `<cefBuildVersion>` is empty, read `cef_build_version` from `cef-version.json`.
  The CI `push` trigger relies on this.
- Map the RID to the CDN architecture: `win-x64` → `windows64`,
  `win-arm64` → `windowsarm64`.
- Download
  `https://cef-builds.spotifycdn.com/cef_binary_<version>_<arch>_minimal.tar.bz2`
  into `redist/tmp-<rid>-<cefVersion>/`, URL-encoding `+` as `%2B`. Reuse an existing
  download and extraction, as the bash scripts do.
- Extract with `tar.exe` (bsdtar, ships in Windows 10+ and on CI runners; it handles bz2).
- Stage `redist/package-<rid>-<cefVersion>/CEF/`.
- Abort with a non-zero exit code if the download fails, the extraction fails, the
  `Release` directory is missing, or `libcef.dll` is absent from the staged output. This
  mirrors the Linux script's guards, which exist because a silently empty staging
  directory produces a valid-looking but empty nupkg.

There is no stripping step: the Windows minimal distribution ships nothing to strip, which
is also why arm64 needs no cross-binutils.

### Payload layout

The CEF 154.0.33 windows64 minimal distribution contains a flat `Release/` (no
subdirectories) and a `Resources/` whose only subdirectory is `locales/`:

```
Release/    bootstrap.exe bootstrapc.exe chrome_elf.dll d3dcompiler_47.dll
            dxcompiler.dll dxil.dll libcef.dll libcef.lib v8_context_snapshot.bin
            vk_swiftshader.dll vk_swiftshader_icd.json vulkan-1.dll
Resources/  chrome_100_percent.pak chrome_200_percent.pak icudtl.dat resources.pak
            locales/
```

(`libEGL.dll` and `libGLESv2.dll` are gone as of CEF 154.)

The script stages a single flat tree: `Release/*` at the root of `CEF/`, then `Resources/*`
overlaid onto that same root, so `libcef.dll`, `icudtl.dat` and the `.pak` files sit side
by side and `locales/` is the only subfolder.

This layout is required, not cosmetic: on Windows `CefRuntimeLoader` never sets
`ResourcesDirPath` or `LocalesDirPath`, so CEF's defaults must hold — resources beside the
loaded module and locales in `locales/`.

Excluded from the staged output: `libcef.lib` (a link-time import library, dead weight for
.NET consumers, and absent from upstream's native folder) and `bootstrap.exe` /
`bootstrapc.exe` (new in CEF 15x for the CDN-installer flow these self-contained packages
deliberately are not). Everything packed lands in every consumer's output and publish
directory, so stray executables would also enter their code-signing scope.

`AddCefRedistToPackage` already globs `redist/package-<rid>-<version>/CEF/**` into
`CEF/<rid>/` and needs no change.

Deliberately absent from the package: any `runtimes/<rid>/native/` folder, so NuGet does no
implicit native-asset copying and the props files are the only mechanism; `include/`
headers; and symbols.

## Consumption design

### Props files

`CefRuntime/cef.runtime.win-x64.props` and `cef.runtime.win-arm64.props`, each packed to
both `build/` and `buildTransitive/`, as the existing per-RID props are.
`buildTransitive` is what carries the behaviour to downstream consumers: `CefGlue.Common`
references the packages with `PrivateAssets="runtime;native"`, so the build logic still
reaches whoever consumes `CefGlue.Next.*` and the copy runs in their executable project.

### Architecture selection

Each props file computes its own effective RID:

1. If `$(RuntimeIdentifier)` is set, use it.
2. Otherwise map `$(Platform)`: `x64` → `win-x64`, `ARM64` → `win-arm64`.
3. Otherwise (including `AnyCPU` and empty) default to `win-x64`.

A props file acts only when the effective RID equals its own, so with both packages
referenced exactly one of them copies. This duplicates the mapping that
`CefGlue.Common.props` already performs, intentionally: a runtime package must work
standalone and must not depend on a CefGlue-defined property.

### Copy targets

Per package, two targets, both additionally guarded by `'$(MSBuildRestoreSessionId)' == ''`
so they never run during restore:

| Target | Hook | Destination |
|---|---|---|
| `CefRedistWinX64CopyResources` | `AfterTargets="Build"` | `$(CefRedistWinX64TargetDir)`, default `$(TargetDir)` |
| `CefRedistWinX64CopyPublishResources` | `AfterTargets="Publish"` | `$(CefRedistWinX64PublishDir)`, default `$(PublishDir)` |

(and the `WinArm64` equivalents). Both copy `CEF/<rid>/**` preserving `%(RecursiveDir)` so
`locales/` stays a subfolder, with `SkipUnchangedFiles="true"`.

The condition uses `$(OutputType.Contains('Exe'))` rather than `'$(OutputType)' == 'Exe'`,
because the WPF and Avalonia demos are `WinExe`. The existing Linux props use the equality
form; that is one reason the Windows props are written fresh rather than copied.

The overridable destination properties follow the existing `CefRedistLinuxX64TargetDir`
naming so consumers who already redirect the Linux copy find the Windows equivalent
predictable.

### Repository wiring

- `Directory.Packages.props`: remove the three `chromiumembeddedframework.runtime*`
  `PackageVersion` entries; add `cef.runtime.win-x64` and `cef.runtime.win-arm64` at
  `$(CefRuntimePackageVersion)`.
- `CefGlue.Packages.props`: replace the three upstream `PackageReference`s with two of
  ours, using `PrivateAssets="runtime;native"` like the Linux and macOS lines. No
  `ExcludeAssets` is needed, because the packages contain no `runtimes/` assets for NuGet
  to copy implicitly.
- `CefGlue.Common.targets`: delete the `CefRedistCopyWindowsResources` target. It exists
  only to `CallTarget` into `CefRedist64CopyResources` / `CefRedistArm64CopyResources`,
  which are defined inside the upstream packages and disappear with them. The Windows copy
  is then driven by our props, like every other platform. `ResolveVcvarsFile` and
  `Editbin` are unrelated and stay untouched.
- `Nuget.config`: no change. Its `packageSourceMapping` already routes `cef.runtime.*` to
  both the `Local` and `Nuget` sources, which covers the new ids.

## CI design

Two new jobs in `.github/workflows/build-cef-packages.yml`, `build-windows-x64` and
`build-windows-arm64`, both on `ubuntu-latest` with `shell: pwsh` (preinstalled on that
image). Packing is RID-agnostic, so a Linux runner produces an identical nupkg and the
workflow avoids Windows runner minutes. `PrepareRedist` therefore invokes the script as
`pwsh -File` off-Windows, and `powershell`/`pwsh` on Windows.

The `packages` workflow input gains a `windows-only` choice. The existing jobs' loose
`if: github.event.inputs.packages != 'macos-only'` conditions are replaced with explicit
per-platform checks, because with a third platform the negative form selects the wrong
jobs.

Each job verifies its artifact before upload: list the nupkg contents, grep for
`libcef.dll`, and fail if the file is smaller than 100 MB. That is the empty-package
failure mode that previously shipped broken Linux and macOS packages.

## Documentation changes

- `CLAUDE.md` gotcha 1 and `UPGRADE.md` Step 1 currently say the official Windows nuget
  package gates the whole upgrade and must be checked first. That stops being true and
  both are rewritten: the only external gate is CDN availability for the needed
  architectures.
- `UPGRADE.md` Step 6 gains the two Windows `dotnet pack` commands; the version-mapping
  and "files modified" tables gain the new package ids.
- `README.md`'s fork-built packages table gains `cef.runtime.win-x64` and
  `cef.runtime.win-arm64` rows.

## Verification

In order, each step gating the next:

1. Pack both packages locally. Expect roughly 130–170 MB each (the Linux packages are
   ~145 MB and the windows64 minimal archive is 165 MB), containing
   `CEF/win-x64/libcef.dll`, `icudtl.dat` and `locales/de.pak`, and containing neither
   `libcef.lib` nor `bootstrap*.exe`. A package under 100 MB means staging silently
   produced nothing.
2. `dotnet restore Xilium.CefGlue.slnx` — the NU1102 blocker is gone and every project
   restores.
3. `dotnet build Xilium.CefGlue.slnx -c Release` — 0 errors.
4. `dotnet test CefGlue.Tests/CefGlue.Tests.csproj -c Release` — 143 total, 142 passed,
   1 skipped, 0 failed, and `libcef.dll` in the test output reports version 154.0.33.
   This is the proof that our package's binaries are the ones actually loaded.
5. `dotnet publish CefGlue.Demo.Avalonia -c Release -r win-x64` — `publish/` contains
   `libcef.dll` and `locales/`. This exercises the new publish hook, which no existing
   package provides.
6. Build a demo with no `RuntimeIdentifier` — natives are still copied, exercising the
   `$(Platform)` fallback.

Nuget.org's per-package limit is 250 MB, so the expected sizes leave headroom; step 1
records the actual figures.

## Implementation notes (2026-10-05)

What the implementation found that the design above did not anticipate:

- **Windows `tar.exe` cannot unpack `.tar.bz2`.** The design assumed the bundled tar handles
  bz2. It does not: bsdtar 3.5.2 as shipped with Windows advertises zlib only and, handed a
  `.tar.bz2`, **hangs indefinitely at 0% CPU** rather than failing — the worst failure mode,
  because a timeout-less build just stops. The staging script therefore decompresses with
  `bzip2` (ships with Git for Windows, already a prerequisite) and hands `tar.exe` a plain
  `.tar`, which it unpacks in about a second. 37 s to decompress, 1 s to untar. Non-Windows
  hosts, including CI, still use `tar -xjf`, so CI gains no new dependency. `bzip2` is now a
  documented Windows prerequisite in UPGRADE.md and CLAUDE.md.
- **Staging guards must judge content, not path existence.** A killed run left an empty
  extraction directory behind, and an existence-only check then reported "already extracted"
  and failed later with a misleading "Release directory not found". The script now looks for
  an actual `Release` directory and re-extracts otherwise, and discards a truncated download.
  (The linux/osx bash scripts still have the original existence-only check.)
- **Actual package sizes: 179.5 MB (win-x64) and 179.8 MB (win-arm64)**, above the design's
  130–170 MB estimate because `libcef.dll` alone is 277 MB uncompressed. Still comfortably
  under nuget.org's 250 MB limit, but with less headroom than assumed — worth re-checking on
  future CEF majors. Staged payload is 399 MB uncompressed per architecture.
- **arm64 got a stronger check than planned.** Rather than only inspecting the file list, the
  PE header of the packaged `libcef.dll` was read and reports machine type `0xaa64` (ARM64),
  confirming the right distribution was downloaded. Running an arm64 app is still untested.

Verified: restore (no NU1102), `dotnet build` 0 errors, 143 tests with 142 passed and 1
skipped, `libcef.dll` 154.0.33 in the test output root and `subprocess/`, publish output
carrying the natives, and a no-RID `WinExe` build copying win-x64 via the `$(Platform)`
fallback.

## Known limits and follow-ups

- **arm64 is verified by package inspection only.** No arm64 Windows machine is available
  here, so "arm64 works" is not claimed beyond correct package contents and a successful
  build.
- **Linux/macOS publish gap.** Their props copy only after `Build`, so `dotnet publish`
  leaves natives out of `publish/`. Not addressed here; follow-up.
- **`win-x86` deferred.** The CDN ships `windows32` and the design extends to it by adding
  a third RID, a props file and a `Platform` mapping. A genuinely 32-bit app would also
  need a 32-bit BrowserProcess and an x86 `editbin`/vcvars path.
- **Stale local extraction.** After rebuilding a package at an unchanged version, clear
  `packages/cef.runtime.win-*/<version>` or NuGet reuses the old copy.
- **Upstream packages are not kept as a fallback.** This is a full replacement; if a
  Windows package ever cannot be built, the fix is to build it, not to fall back.
