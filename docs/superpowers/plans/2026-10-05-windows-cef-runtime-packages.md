# Windows CEF Runtime Packages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and consume fork-maintained `cef.runtime.win-x64` and `cef.runtime.win-arm64` packages so CEF 154.0.33 restores and runs on Windows without the upstream `chromiumembeddedframework.runtime*` packages.

**Architecture:** The existing `CefRuntime/CefRuntime.csproj` gains the two Windows RIDs. A new PowerShell staging script downloads the CEF Windows minimal distribution and flattens `Release/` + `Resources/` into one tree, which the existing `AddCefRedistToPackage` target packs to `CEF/<rid>/`. Two new per-RID props files, shipped in `build/` and `buildTransitive/`, copy that tree into the output directory on build and into the publish directory on publish.

**Tech Stack:** MSBuild (props/targets, `dotnet pack`), PowerShell 5.1-compatible script using `curl.exe` and `tar.exe`, GitHub Actions, .NET 10 SDK.

**Spec:** `docs/superpowers/specs/2026-10-05-windows-cef-runtime-packages-design.md`

---

## Conventions for every task

- **Branch:** `build/cef-154.0.33`. Do not create new branches.
- **Line endings matter.** Tracked `.cs`/`.py`/`.md` files in this repo are LF; `README.md`,
  `CefVersion.props` and `.github/workflows/build-cef-packages.yml` are **CRLF**. Git Bash
  `sed -i` silently strips CRLF and churns the whole file. Before editing, check with:
  ```bash
  python -c "b=open('<file>','rb').read(); print('crlf' if b'\r\n' in b else 'lf')"
  ```
  After editing, `git diff --stat <file>` must show only the lines you intended. If the whole
  file churns, restore the original ending with a byte-level pass:
  ```bash
  python -c "
  f='<file>'; b=open(f,'rb').read().replace(b'\r\n',b'\n').replace(b'\n',b'\r\n'); open(f,'wb').write(b)"
  ```
- **Commit messages:** Conventional Commits. No `Co-Authored-By` or "Generated with" trailers.
- **No TDD unit tests here.** This is build/packaging code; there is no test framework that can
  assert on MSBuild behaviour in this repo. Each task therefore has an explicit verification step
  with a command and expected output, and a failing verification blocks the next task.
- **Working directory** is the repo root `D:\VSNet\CefGlue` unless a step says otherwise.

---

## Task 1: Windows redist staging script

Downloads the CEF Windows minimal distribution and stages a flat `CEF/` tree.

**Files:**
- Create: `CefRuntime/make_cefredist_windows.ps1`

- [ ] **Step 1: Verify the script does not exist and the staging is therefore impossible**

Run:
```bash
ls CefRuntime/make_cefredist_windows.ps1
```
Expected: `No such file or directory`.

- [ ] **Step 2: Create `CefRuntime/make_cefredist_windows.ps1`**

Write exactly this content. It must stay PowerShell 5.1-compatible (no ternary, no `??`),
because on a Windows dev box it runs under `powershell`, not `pwsh`.

```powershell
#!/usr/bin/env pwsh
#
# Stages the CEF Windows release distribution for packing into cef.runtime.win-<arch>.
#
# Mirrors make_cefredist_linux.sh / make_cefredist_osx.sh: same argument contract, same
# redist/ layout, same fail-fast guards. Unlike those, it needs no WSL and no strip step
# (the Windows minimal distribution ships nothing to strip).
#
# Usage: make_cefredist_windows.ps1 <rid> [<cefBuildVersion>]
#   rid              win-x64 | win-arm64
#   cefBuildVersion  e.g. 154.0.33+ga03e714+chromium-154.0.8037.94
#                    When omitted, read from ../cef-version.json (the CI push trigger
#                    relies on this).

param(
    [Parameter(Mandatory = $true)][string] $Rid,
    [string] $CefBuildVersion
)

$ErrorActionPreference = 'Stop'

# Files CEF ships that an embedder never loads. libcef.lib is a link-time import library
# for C++ consumers; bootstrap.exe/bootstrapc.exe serve CEF's CDN-installer flow, which
# these self-contained packages are not. Everything packed lands in every consumer's
# output, publish folder and code-signing scope, so these are dropped.
$ExcludedFiles = @('libcef.lib', 'bootstrap.exe', 'bootstrapc.exe')

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($CefBuildVersion)) {
    $versionFile = Join-Path $ScriptDir '../cef-version.json'
    if (-not (Test-Path $versionFile)) {
        throw "cef-version.json not found at $versionFile"
    }
    $match = [regex]::Match((Get-Content -Raw $versionFile), '"cef_build_version"\s*:\s*"([^"]+)"')
    if (-not $match.Success) {
        throw "cef_build_version not found in $versionFile"
    }
    $CefBuildVersion = $match.Groups[1].Value
}

$CefVersion = $CefBuildVersion.Split('+')[0]

switch ($Rid) {
    'win-x64'   { $arch = 'windows64' }
    'win-arm64' { $arch = 'windowsarm64' }
    default     { throw "Unsupported Windows RID '$Rid' (expected win-x64 or win-arm64)" }
}

Push-Location $ScriptDir
try {
    $base = 'redist'
    $tmp = Join-Path $base "tmp-$Rid-$CefVersion"
    $output = Join-Path $base "package-$Rid-$CefVersion"
    $archive = Join-Path $tmp "cef-$CefVersion.tar.bz2"
    $binaries = Join-Path $tmp "cef_binaries-$CefVersion"

    New-Item -ItemType Directory -Force -Path $tmp | Out-Null

    if (-not (Test-Path $archive)) {
        $encodedVersion = $CefBuildVersion.Replace('+', '%2B')
        $url = "https://cef-builds.spotifycdn.com/cef_binary_${encodedVersion}_${arch}_minimal.tar.bz2"
        Write-Host "Downloading CEF binaries v$CefVersion-$arch"
        & curl.exe -fL --retry 3 -o $archive $url
        if ($LASTEXITCODE -ne 0) {
            Remove-Item -Force -ErrorAction SilentlyContinue $archive
            throw "Download failed for $url"
        }
    }
    else {
        Write-Host "CEF binaries v$CefVersion-$arch already downloaded"
    }

    if (-not (Test-Path $binaries)) {
        Write-Host "Extracting CEF binaries v$CefVersion-$arch"
        New-Item -ItemType Directory -Force -Path $binaries | Out-Null
        & tar.exe -xjf $archive -C $binaries
        if ($LASTEXITCODE -ne 0) {
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $binaries
            throw "Extraction failed for $archive"
        }
    }
    else {
        Write-Host "CEF binaries v$CefVersion-$arch already extracted"
    }

    $releaseDir = Get-ChildItem -Path $binaries -Directory -Recurse -Filter 'Release' | Select-Object -First 1
    if ($null -eq $releaseDir) {
        throw "Release directory not found in $binaries"
    }
    $resourcesDir = Get-ChildItem -Path $binaries -Directory -Recurse -Filter 'Resources' | Select-Object -First 1
    if ($null -eq $resourcesDir) {
        throw "Resources directory not found in $binaries"
    }

    if (Test-Path $output) {
        Remove-Item -Recurse -Force $output
    }
    $cefDir = Join-Path $output 'CEF'
    New-Item -ItemType Directory -Force -Path $cefDir | Out-Null

    # One flat tree: Release/* at the root, then Resources/* overlaid onto the same root.
    # CefRuntimeLoader does not set ResourcesDirPath/LocalesDirPath on Windows, so CEF's
    # defaults must hold: resources beside the module, locales in locales/.
    Write-Host 'Copying CEF binaries...'
    Copy-Item -Path (Join-Path $releaseDir.FullName '*') -Destination $cefDir -Recurse -Force
    Write-Host 'Copying CEF resources...'
    Copy-Item -Path (Join-Path $resourcesDir.FullName '*') -Destination $cefDir -Recurse -Force

    foreach ($name in $ExcludedFiles) {
        Get-ChildItem -Path $cefDir -Filter $name -File -Recurse | ForEach-Object {
            Write-Host "Excluding $($_.Name)"
            Remove-Item -Force $_.FullName
        }
    }

    $libcef = Join-Path $cefDir 'libcef.dll'
    if (-not (Test-Path $libcef)) {
        throw "libcef.dll not found in $cefDir"
    }
    $icudtl = Join-Path $cefDir 'icudtl.dat'
    if (-not (Test-Path $icudtl)) {
        throw "icudtl.dat not found in $cefDir"
    }
    if (-not (Test-Path (Join-Path $cefDir 'locales'))) {
        throw "locales directory not found in $cefDir"
    }

    $totalMb = [Math]::Round(((Get-ChildItem -Path $cefDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
    Write-Host "Staged $Rid CEF $CefVersion into $cefDir ($totalMb MB)"
}
finally {
    Pop-Location
}
```

- [ ] **Step 3: Run the script for win-x64**

Run (PowerShell, from the repo root):
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File CefRuntime\make_cefredist_windows.ps1 win-x64 "154.0.33+ga03e714+chromium-154.0.8037.94"
```
Expected: downloads (or reuses) the archive, extracts it, prints `Excluding libcef.lib`,
`Excluding bootstrap.exe`, `Excluding bootstrapc.exe`, then
`Staged win-x64 CEF 154.0.33 into redist\package-win-x64-154.0.33\CEF (<n> MB)` with a size
of roughly 200–270 MB uncompressed. Exit code 0.

- [ ] **Step 4: Verify the staged tree is flat, complete and filtered**

Run:
```bash
D=CefRuntime/redist/package-win-x64-154.0.33/CEF
ls "$D" | head -20
echo "locales: $(ls "$D/locales" | wc -l)"
ls "$D"/libcef.lib "$D"/bootstrap.exe "$D"/bootstrapc.exe 2>&1 | tail -3
```
Expected: `libcef.dll`, `icudtl.dat`, `chrome_elf.dll`, the `.pak` files and a `locales`
directory at the top level; `locales` holds over 100 files; the three excluded names all
report `No such file or directory`.

- [ ] **Step 5: Verify the version-file fallback works**

Run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File CefRuntime\make_cefredist_windows.ps1 win-x64
```
Expected: same completion, and it reports "already downloaded"/"already extracted" because it
resolved the identical version from `cef-version.json`. Exit code 0.

- [ ] **Step 6: Verify an unsupported RID fails loudly**

Run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File CefRuntime\make_cefredist_windows.ps1 win-x86; echo "exit=$LASTEXITCODE"
```
Expected: the error `Unsupported Windows RID 'win-x86' (expected win-x64 or win-arm64)` and a
non-zero exit code.

- [ ] **Step 7: Commit**

`CefRuntime/redist/` is build output; confirm it is ignored (`git status --short` must not list
it) and commit only the script.

```bash
git add CefRuntime/make_cefredist_windows.ps1
git commit -m "build(runtime-packages): add Windows CEF redist staging script

Downloads the CEF Windows minimal distribution and stages one flat CEF/ tree
(Release/* with Resources/* overlaid) for packing, dropping libcef.lib and the
bootstrap executables that an embedder never loads. Same argument contract and
fail-fast guards as the linux/osx scripts, but needs no WSL and no strip step."
```

---

## Task 2: Teach CefRuntime.csproj the Windows RIDs

**Files:**
- Modify: `CefRuntime/CefRuntime.csproj` (the `RuntimeIdentifiers`/`CurrentPlatform` property group and the `PrepareRedist` target)

- [ ] **Step 1: Verify packing a Windows RID currently fails**

Run:
```powershell
dotnet pack CefRuntime\CefRuntime.csproj --runtime win-x64 "/p:CefBuildVersion=154.0.33+ga03e714+chromium-154.0.8037.94" -c Release -v:m 2>&1 | Select-String -Pattern "error|NETSDK" | Select-Object -First 5
```
Expected: it fails, because `win-x64` is not in `RuntimeIdentifiers` and `PrepareRedist` would
run the Linux bash script. Record the error text; the fix in step 2 must address it.

- [ ] **Step 2: Add the Windows RIDs and the `win` host platform**

In `CefRuntime/CefRuntime.csproj`, replace:

```xml
    <RuntimeIdentifiers>osx-arm64;osx-x64;linux-arm64;linux-x64</RuntimeIdentifiers>
    <CurrentPlatform>osx</CurrentPlatform>
    <CurrentPlatform Condition="$([MSBuild]::IsOSPlatform('Linux'))">linux</CurrentPlatform>
```

with:

```xml
    <RuntimeIdentifiers>osx-arm64;osx-x64;linux-arm64;linux-x64;win-x64;win-arm64</RuntimeIdentifiers>
    <CurrentPlatform>osx</CurrentPlatform>
    <CurrentPlatform Condition="$([MSBuild]::IsOSPlatform('Linux'))">linux</CurrentPlatform>
    <CurrentPlatform Condition="$([MSBuild]::IsOSPlatform('Windows'))">win</CurrentPlatform>
```

This also makes a bare `dotnet pack` on Windows build `win-arm64` plus `win-x64` as the
secondary runtime, instead of attempting `osx-arm64` through WSL.

- [ ] **Step 3: Route Windows RIDs to the PowerShell script**

In the same file, replace the whole body of the `PrepareRedist` target (from its
`<PropertyGroup>` through the last `<Exec .../>`) with:

```xml
    <PropertyGroup>
      <IsWindows Condition="$([MSBuild]::IsOSPlatform('Windows'))">true</IsWindows>
      <IsWindowsRedist Condition="$(RuntimeIdentifier.StartsWith('win-'))">true</IsWindowsRedist>
      <RedistBuildScript>$(MSBuildThisFileDirectory)make_cefredist_linux.sh</RedistBuildScript>
      <RedistBuildScript Condition="$(RuntimeIdentifier.StartsWith('osx-'))">$(MSBuildThisFileDirectory)make_cefredist_osx.sh</RedistBuildScript>
      <RedistBuildScript Condition="'$(IsWindowsRedist)' == 'true'">$(MSBuildThisFileDirectory)make_cefredist_windows.ps1</RedistBuildScript>
      <!-- Windows hosts always have powershell (5.1); every other host uses pwsh, which is
           preinstalled on the CI images. The script is 5.1-compatible for this reason. -->
      <PowerShellExe Condition="'$(PowerShellExe)' == '' AND '$(IsWindows)' == 'true'">%WINDIR%\System32\WindowsPowerShell\v1.0\powershell.exe</PowerShellExe>
      <PowerShellExe Condition="'$(PowerShellExe)' == ''">pwsh</PowerShellExe>
      <ScriptShell>$(MSBuildThisFileDirectory)make_cefredist.ps1</ScriptShell>
    </PropertyGroup>

    <!-- Windows redist: run the staging script directly, on any host, no WSL involved. -->
    <Exec Command="&quot;$(PowerShellExe)&quot; -NoProfile -ExecutionPolicy Bypass -File &quot;$(RedistBuildScript)&quot; &quot;$(RuntimeIdentifier)&quot; &quot;$(CefBuildVersion)&quot;"
          Condition="'$(IsWindowsRedist)' == 'true'" />

    <!-- Linux/macOS redist on a POSIX host: run the bash script directly. -->
    <Exec Command="chmod 755 $(RedistBuildScript)" Condition="'$(IsWindows)' != 'true' AND '$(IsWindowsRedist)' != 'true'" />
    <Exec Command="$(RedistBuildScript) $(RuntimeIdentifier) $(CefBuildVersion)" Condition="'$(IsWindows)' != 'true' AND '$(IsWindowsRedist)' != 'true'" />

    <!-- Linux/macOS redist on a Windows host: via WSL. -->
    <Exec Command="$(PowerShellExe) -File &quot;$(ScriptShell)&quot; &quot;$(RedistBuildScript)&quot; &quot;$(RuntimeIdentifier)&quot; &quot;$(CefBuildVersion)&quot;"
          Condition="'$(IsWindows)' == 'true' AND '$(IsWindowsRedist)' != 'true'" />
```

- [ ] **Step 4: Verify the Linux/macOS paths are unchanged in behaviour**

Run:
```bash
git diff CefRuntime/CefRuntime.csproj
```
Expected: the only changes are the two property additions, the new `IsWindowsRedist`/
`PowerShellExe` properties, the new Windows `Exec`, and `AND '$(IsWindowsRedist)' != 'true'`
appended to the three pre-existing `Exec` conditions. The `osx`/`linux` script selection and
the WSL wrapper invocation are otherwise untouched.

- [ ] **Step 5: Commit**

```bash
git add CefRuntime/CefRuntime.csproj
git commit -m "build(runtime-packages): pack win-x64 and win-arm64 from CefRuntime

Add the two Windows RIDs and route them to make_cefredist_windows.ps1, run
directly rather than through the WSL wrapper the linux/osx scripts need. A bare
pack on a Windows host now stages win-* instead of attempting osx-arm64."
```

---

## Task 3: Per-RID consumption props

**Files:**
- Create: `CefRuntime/cef.runtime.win-x64.props`
- Create: `CefRuntime/cef.runtime.win-arm64.props`

- [ ] **Step 1: Create `CefRuntime/cef.runtime.win-x64.props`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="4.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <!--
    Copies the CEF win-x64 runtime next to a consuming executable, on build and on publish.

    Every condition is evaluated when the target runs, never at import time: package props
    are imported before the consuming project's own PropertyGroups, so $(RuntimeIdentifier),
    $(Platform) and $(OutputType) are not reliably set yet while this file is being read.

    Arch selection: an explicit win-x64 RID always wins. With no RID at all we fall back to
    $(Platform) on a Windows host and treat anything that is not ARM64 (x64, AnyCPU, unset)
    as win-x64, which keeps plain `dotnet build`, VS F5 and `dotnet test` working. The host
    check keeps a RID-less Linux or macOS build from copying Windows binaries.

    $(OutputType.Contains('Exe')) rather than == 'Exe' so WinExe projects (WPF, Avalonia) are
    covered too.
  -->
  <Target Name="CefRedistWinX64CopyResources" AfterTargets="Build"
          Condition="'$(MSBuildRestoreSessionId)' == '' AND $(OutputType.Contains('Exe')) AND ('$(RuntimeIdentifier)' == 'win-x64' OR ('$(RuntimeIdentifier)' == '' AND $([MSBuild]::IsOSPlatform('Windows')) AND '$(Platform)' != 'ARM64'))">
    <PropertyGroup>
      <CefRedistWinX64TargetDir Condition="'$(CefRedistWinX64TargetDir)' == ''">$(TargetDir)</CefRedistWinX64TargetDir>
    </PropertyGroup>

    <ItemGroup>
      <_CefRedistWinX64File Include="$(MSBuildThisFileDirectory)..\CEF\win-x64\**\*.*" />
    </ItemGroup>

    <Message Importance="high" Text="Copying Chromium Embedded Framework Runtime win-x64 files to $(CefRedistWinX64TargetDir)" />
    <Copy SourceFiles="@(_CefRedistWinX64File)"
          DestinationFiles="@(_CefRedistWinX64File->'$(CefRedistWinX64TargetDir)%(RecursiveDir)%(Filename)%(Extension)')"
          SkipUnchangedFiles="true" />
  </Target>

  <Target Name="CefRedistWinX64CopyPublishResources" AfterTargets="Publish"
          Condition="'$(MSBuildRestoreSessionId)' == '' AND $(OutputType.Contains('Exe')) AND ('$(RuntimeIdentifier)' == 'win-x64' OR ('$(RuntimeIdentifier)' == '' AND $([MSBuild]::IsOSPlatform('Windows')) AND '$(Platform)' != 'ARM64'))">
    <PropertyGroup>
      <CefRedistWinX64PublishDir Condition="'$(CefRedistWinX64PublishDir)' == ''">$(PublishDir)</CefRedistWinX64PublishDir>
    </PropertyGroup>

    <ItemGroup>
      <_CefRedistWinX64PublishFile Include="$(MSBuildThisFileDirectory)..\CEF\win-x64\**\*.*" />
    </ItemGroup>

    <Message Importance="high" Text="Publishing Chromium Embedded Framework Runtime win-x64 files to $(CefRedistWinX64PublishDir)" />
    <Copy SourceFiles="@(_CefRedistWinX64PublishFile)"
          DestinationFiles="@(_CefRedistWinX64PublishFile->'$(CefRedistWinX64PublishDir)%(RecursiveDir)%(Filename)%(Extension)')"
          SkipUnchangedFiles="true" />
  </Target>
</Project>
```

- [ ] **Step 2: Create `CefRuntime/cef.runtime.win-arm64.props`**

Same shape, with the ARM64 arch test inverted (`'$(Platform)' == 'ARM64'`):

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="4.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <!--
    Copies the CEF win-arm64 runtime next to a consuming executable, on build and on publish.

    Every condition is evaluated when the target runs, never at import time: package props
    are imported before the consuming project's own PropertyGroups, so $(RuntimeIdentifier),
    $(Platform) and $(OutputType) are not reliably set yet while this file is being read.

    Arch selection: an explicit win-arm64 RID always wins. With no RID at all we fall back to
    $(Platform) == 'ARM64' on a Windows host; every other RID-less case is win-x64's to serve,
    so exactly one of the two packages copies.

    $(OutputType.Contains('Exe')) rather than == 'Exe' so WinExe projects (WPF, Avalonia) are
    covered too.
  -->
  <Target Name="CefRedistWinArm64CopyResources" AfterTargets="Build"
          Condition="'$(MSBuildRestoreSessionId)' == '' AND $(OutputType.Contains('Exe')) AND ('$(RuntimeIdentifier)' == 'win-arm64' OR ('$(RuntimeIdentifier)' == '' AND $([MSBuild]::IsOSPlatform('Windows')) AND '$(Platform)' == 'ARM64'))">
    <PropertyGroup>
      <CefRedistWinArm64TargetDir Condition="'$(CefRedistWinArm64TargetDir)' == ''">$(TargetDir)</CefRedistWinArm64TargetDir>
    </PropertyGroup>

    <ItemGroup>
      <_CefRedistWinArm64File Include="$(MSBuildThisFileDirectory)..\CEF\win-arm64\**\*.*" />
    </ItemGroup>

    <Message Importance="high" Text="Copying Chromium Embedded Framework Runtime win-arm64 files to $(CefRedistWinArm64TargetDir)" />
    <Copy SourceFiles="@(_CefRedistWinArm64File)"
          DestinationFiles="@(_CefRedistWinArm64File->'$(CefRedistWinArm64TargetDir)%(RecursiveDir)%(Filename)%(Extension)')"
          SkipUnchangedFiles="true" />
  </Target>

  <Target Name="CefRedistWinArm64CopyPublishResources" AfterTargets="Publish"
          Condition="'$(MSBuildRestoreSessionId)' == '' AND $(OutputType.Contains('Exe')) AND ('$(RuntimeIdentifier)' == 'win-arm64' OR ('$(RuntimeIdentifier)' == '' AND $([MSBuild]::IsOSPlatform('Windows')) AND '$(Platform)' == 'ARM64'))">
    <PropertyGroup>
      <CefRedistWinArm64PublishDir Condition="'$(CefRedistWinArm64PublishDir)' == ''">$(PublishDir)</CefRedistWinArm64PublishDir>
    </PropertyGroup>

    <ItemGroup>
      <_CefRedistWinArm64PublishFile Include="$(MSBuildThisFileDirectory)..\CEF\win-arm64\**\*.*" />
    </ItemGroup>

    <Message Importance="high" Text="Publishing Chromium Embedded Framework Runtime win-arm64 files to $(CefRedistWinArm64PublishDir)" />
    <Copy SourceFiles="@(_CefRedistWinArm64PublishFile)"
          DestinationFiles="@(_CefRedistWinArm64PublishFile->'$(CefRedistWinArm64PublishDir)%(RecursiveDir)%(Filename)%(Extension)')"
          SkipUnchangedFiles="true" />
  </Target>
</Project>
```

No change is needed to pack them: `CefRuntime.csproj` already packs
`cef.runtime.$(RuntimeIdentifier).props` into both `build` and `buildTransitive`.

- [ ] **Step 3: Commit**

```bash
git add CefRuntime/cef.runtime.win-x64.props CefRuntime/cef.runtime.win-arm64.props
git commit -m "build(runtime-packages): add Windows consumption props

One props file per Windows RID, copying the packaged CEF tree next to a
consuming executable after Build and after Publish. Conditions are evaluated at
target execution time because package props are imported before the consuming
project sets RuntimeIdentifier/Platform/OutputType."
```

---

## Task 4: Pack both Windows packages

**Files:** none modified; produces `LocalPackages/cef.runtime.win-*.154.0.33.nupkg`

- [ ] **Step 1: Pack win-x64**

Run:
```powershell
dotnet pack CefRuntime\CefRuntime.csproj --runtime win-x64 "/p:CefBuildVersion=154.0.33+ga03e714+chromium-154.0.8037.94" -c Release -v:m 2>&1 | Select-Object -Last 5
```
Expected: `cef.runtime.win-x64.154.0.33.nupkg` created under `LocalPackages`, exit code 0.

- [ ] **Step 2: Pack win-arm64**

Run:
```powershell
dotnet pack CefRuntime\CefRuntime.csproj --runtime win-arm64 "/p:CefBuildVersion=154.0.33+ga03e714+chromium-154.0.8037.94" -c Release -v:m 2>&1 | Select-Object -Last 5
```
Expected: `cef.runtime.win-arm64.154.0.33.nupkg` created, exit code 0.

- [ ] **Step 3: Verify size**

Run:
```powershell
Get-ChildItem LocalPackages\cef.runtime.win-*.154.0.33.nupkg | ForEach-Object { "{0} {1:N1} MB" -f $_.Name, ($_.Length/1MB) }
```
Expected: both well over 100 MB (roughly 130–170 MB) and under nuget.org's 250 MB limit. A
package under 100 MB means staging produced nothing and the task must not be committed.

- [ ] **Step 4: Verify contents**

Run:
```bash
for RID in win-x64 win-arm64; do
  echo "=== $RID ==="
  unzip -l LocalPackages/cef.runtime.$RID.154.0.33.nupkg | grep -cE "CEF/$RID/locales/.*\.pak" | sed 's/^/locale paks: /'
  unzip -l LocalPackages/cef.runtime.$RID.154.0.33.nupkg | grep -E "CEF/$RID/(libcef\.dll|icudtl\.dat|resources\.pak)$"
  unzip -l LocalPackages/cef.runtime.$RID.154.0.33.nupkg | grep -E "(build|buildTransitive)/cef\.runtime\.$RID\.props"
  unzip -l LocalPackages/cef.runtime.$RID.154.0.33.nupkg | grep -E "libcef\.lib|bootstrap" || echo "excluded files absent: OK"
done
```
Expected, per RID: over 100 locale `.pak` entries; `libcef.dll`, `icudtl.dat` and
`resources.pak` directly under `CEF/<rid>/`; the props file present in **both** `build/` and
`buildTransitive/`; and `excluded files absent: OK`.

- [ ] **Step 5: Commit**

Nothing to commit (packages are build output under the ignored `LocalPackages/`). Record the
measured sizes in the task notes for the spec's verification section.

---

## Task 5: Switch the repository to the new packages

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `CefGlue.Packages.props`
- Modify: `CefGlue.Common/build/CefGlue.Common.targets`

- [ ] **Step 1: Confirm the current failure**

Run:
```powershell
dotnet restore Xilium.CefGlue.slnx 2>&1 | Select-String -Pattern "NU1102" | Select-Object -First 3
```
Expected: `NU1102 ... chromiumembeddedframework.runtime.win-x64 (>= 154.0.33) ... nearest: 152.0.10`.
This is the blocker the task removes.

- [ ] **Step 2: Replace the package versions**

In `Directory.Packages.props`, replace:

```xml
        <PackageVersion Include="chromiumembeddedframework.runtime" Version="$(CefVersion)" />
        <PackageVersion Include="chromiumembeddedframework.runtime.win-x64" Version="$(CefVersion)" />
        <PackageVersion Include="chromiumembeddedframework.runtime.win-arm64" Version="$(CefVersion)" />
```

with:

```xml
        <PackageVersion Include="cef.runtime.win-x64" Version="$(CefRuntimePackageVersion)" />
        <PackageVersion Include="cef.runtime.win-arm64" Version="$(CefRuntimePackageVersion)" />
```

- [ ] **Step 3: Replace the package references**

In `CefGlue.Packages.props`, replace:

```xml
        <!-- Windows -->
        <PackageReference PrivateAssets="runtime;native" ExcludeAssets="runtime;native" Include="chromiumembeddedframework.runtime.win-x64"/>
        <PackageReference PrivateAssets="runtime;native" ExcludeAssets="runtime;native" Include="chromiumembeddedframework.runtime.win-arm64" />
        <PackageReference PrivateAssets="runtime;native" ExcludeAssets="runtime;native" Include="chromiumembeddedframework.runtime" />
```

with:

```xml
        <!-- Windows -->
        <PackageReference PrivateAssets="runtime;native" Include="cef.runtime.win-x64" />
        <PackageReference PrivateAssets="runtime;native" Include="cef.runtime.win-arm64" />
```

`ExcludeAssets` is dropped on purpose: it existed to suppress the upstream packages'
`runtimes/<rid>/native` assets, and our packages ship none.

- [ ] **Step 4: Remove the target that called into the upstream packages**

In `CefGlue.Common/build/CefGlue.Common.targets`, delete this whole target:

```xml
  <!-- Copy CEF DLLs and resources on Windows, on macOS and linux this happening automatically -->
  <Target Name="CefRedistCopyWindowsResources" AfterTargets="Build" Condition="'$(MSBuildRestoreSessionId)' == '' AND $([MSBuild]::IsOSPlatform('Windows')) AND $(OutputType.Contains('Exe'))">
    <Message Importance="high" Text="Copying CEF resources for Windows $(RuntimeIdentifier) ..." />
    
    <CallTarget Targets="CefRedist64CopyResources" Condition="'$(RuntimeIdentifier)' == 'win-x64' OR '$(RuntimeIdentifier)' == ''" />
    <CallTarget Targets="CefRedistArm64CopyResources" Condition="'$(RuntimeIdentifier)' == 'win-arm64'" />
  </Target>
```

It only forwards to `CefRedist64CopyResources`/`CefRedistArm64CopyResources`, which are
defined inside the upstream packages and disappear with them; our props now own the copy.
Leave `ResolveVcvarsFile` and `Editbin` untouched.

- [ ] **Step 5: Verify restore now succeeds**

Run:
```powershell
dotnet restore Xilium.CefGlue.slnx 2>&1 | Select-String -Pattern "NU1102|NU1101|error|wiederhergestellt" | Select-Object -Last 10
```
Expected: no `NU1102`/`NU1101`, every project restored. If NuGet reuses a stale extraction,
clear `packages/cef.runtime.win-*/154.0.33` and retry.

- [ ] **Step 6: Verify no upstream reference survives**

Run:
```bash
grep -rn "chromiumembeddedframework" --include=*.props --include=*.targets --include=*.csproj --include=*.config . | grep -v "^./packages/" || echo "no upstream references: OK"
```
Expected: `no upstream references: OK`.

- [ ] **Step 7: Verify line endings did not churn**

Run:
```bash
git diff --stat Directory.Packages.props CefGlue.Packages.props CefGlue.Common/build/CefGlue.Common.targets
```
Expected: a handful of changed lines per file, not whole-file rewrites. If a file churned
entirely, restore its original ending with the byte-level pass from the conventions section.

- [ ] **Step 8: Build**

Run:
```powershell
dotnet build Xilium.CefGlue.slnx -c Release --no-restore 2>&1 | Select-String -Pattern "error|Fehler|Buildvorgang" | Select-Object -Last 10
```
Expected: 0 errors.

- [ ] **Step 9: Commit**

```bash
git add Directory.Packages.props CefGlue.Packages.props CefGlue.Common/build/CefGlue.Common.targets
git commit -m "build: consume the fork's own Windows CEF runtime packages

Replace chromiumembeddedframework.runtime{,.win-x64,.win-arm64} with
cef.runtime.win-x64/win-arm64 at \$(CefRuntimePackageVersion), and drop the
CefGlue.Common target that only forwarded to copy targets defined inside those
upstream packages. Windows now resolves and copies exactly like linux and macOS,
which unblocks CEF 154.0.33 (upstream's newest is 152.0.10)."
```

---

## Task 6: Verify the runtime actually loads our binaries

**Files:** none modified

- [ ] **Step 1: Run the test suite**

Run:
```powershell
dotnet test CefGlue.Tests/CefGlue.Tests.csproj -c Release 2>&1 | Select-String -Pattern "Bestanden!|Fehler!|erfolgreich:" | Select-Object -Last 5
```
Expected: `gesamt: 143`, `erfolgreich: 142`, `übersprungen: 1`, `Fehler: 0`. A total below 143
means a fixture was dropped from discovery, which is a failure even with 0 reported failures.

- [ ] **Step 2: Verify the loaded CEF version came from our package**

Run:
```powershell
Get-ChildItem CefGlue.Tests\bin\Release -Recurse -Filter libcef.dll | ForEach-Object { "{0}  {1}" -f $_.VersionInfo.ProductVersion, $_.FullName }
```
Expected: every path under `bin\Release` reports `154.0.33+ga03e714+chromium-154.0.8037.94`,
including the `subprocess\` copy. This is the proof that the fork's own package supplied the
binaries.

- [ ] **Step 3: Verify the resource layout in the output**

Run:
```bash
D=CefGlue.Tests/bin/Release/net10.0
ls "$D"/libcef.dll "$D"/icudtl.dat "$D"/resources.pak >/dev/null && echo "runtime files beside the exe: OK"
echo "locales: $(ls "$D/locales" | wc -l)"
```
Expected: `runtime files beside the exe: OK` and over 100 locale files, matching what CEF's
default resource resolution needs on Windows.

- [ ] **Step 4: Verify publish copies the natives**

Run:
```powershell
dotnet publish CefGlue.Demo.Avalonia\CefGlue.Demo.Avalonia.csproj -c Release -r win-x64 --self-contained false -o $env:TEMP\cefpub 2>&1 | Select-Object -Last 3
Get-ChildItem $env:TEMP\cefpub\libcef.dll, $env:TEMP\cefpub\icudtl.dat | Select-Object Name
"locales: $((Get-ChildItem $env:TEMP\cefpub\locales).Count)"
```
Expected: publish succeeds, `libcef.dll` and `icudtl.dat` are in the publish folder, and
`locales` holds over 100 files. This exercises the publish hook that no previous package had.

- [ ] **Step 5: Verify the no-RID fallback**

Run:
```powershell
Remove-Item -Recurse -Force CefGlue.Demo.WPF\bin\Release -ErrorAction SilentlyContinue
dotnet build CefGlue.Demo.WPF\CefGlue.Demo.WPF.csproj -c Release 2>&1 | Select-String -Pattern "win-x64 files to|error" | Select-Object -First 3
Get-ChildItem CefGlue.Demo.WPF\bin\Release -Recurse -Filter libcef.dll | ForEach-Object { $_.FullName }
```
Expected: the `Copying Chromium Embedded Framework Runtime win-x64 files to ...` message
appears and `libcef.dll` lands in the output, proving the `$(Platform)` fallback works for a
`WinExe` project built without a RID.

- [ ] **Step 6: Commit**

Nothing to commit. If any step failed, fix the props or script in the owning task and re-run
this task from step 1.

---

## Task 7: CI jobs for the Windows packages

**Files:**
- Modify: `.github/workflows/build-cef-packages.yml` (CRLF file — see conventions)

- [ ] **Step 1: Add the `windows-only` choice**

Replace:

```yaml
                options:
                    - all
                    - linux-only
                    - macos-only
```

with:

```yaml
                options:
                    - all
                    - linux-only
                    - macos-only
                    - windows-only
```

- [ ] **Step 2: Make the existing job conditions explicit**

The current negative conditions select the wrong jobs once a third platform exists. In the two
Linux jobs replace:

```yaml
        if: ${{ github.event.inputs.packages != 'macos-only' }}
```

with:

```yaml
        if: ${{ github.event.inputs.packages == '' || github.event.inputs.packages == 'all' || github.event.inputs.packages == 'linux-only' }}
```

and in the two macOS jobs replace:

```yaml
        if: ${{ github.event.inputs.packages != 'linux-only' }}
```

with:

```yaml
        if: ${{ github.event.inputs.packages == '' || github.event.inputs.packages == 'all' || github.event.inputs.packages == 'macos-only' }}
```

The `== ''` arm keeps the `push` trigger (which supplies no inputs) building everything, which
is what the current negative conditions do by accident.

- [ ] **Step 3: Add the two Windows jobs**

Insert immediately before the `# Summary job that collects all artifacts` comment:

```yaml
    build-windows-x64:
        name: Build Windows x64 Package
        runs-on: ubuntu-latest
        if: ${{ github.event.inputs.packages == '' || github.event.inputs.packages == 'all' || github.event.inputs.packages == 'windows-only' }}

        steps:
            - name: Checkout repository
              uses: actions/checkout@v4

            - name: Create NuGet package
              working-directory: CefRuntime
              run: |
                  dotnet pack CefRuntime.csproj --runtime win-x64 /p:CefBuildVersion=${{ github.event.inputs.cefbuildversion }}

            - name: Verify NuGet package contents
              run: |
                  echo "=== NuGet Package Contents ==="
                  unzip -l LocalPackages/*.nupkg | head -50
                  echo ""
                  echo "Checking for libcef.dll in package..."
                  unzip -l LocalPackages/*.nupkg | grep libcef.dll
                  echo "Checking the package is not empty..."
                  SIZE=$(stat -c%s LocalPackages/cef.runtime.win-x64.*.nupkg)
                  echo "Package size: $((SIZE / 1048576)) MB"
                  test "$SIZE" -gt 104857600

            - name: Upload artifact
              uses: actions/upload-artifact@v4
              with:
                  name: cef.runtime.win-x64
                  path: LocalPackages/*.nupkg
                  retention-days: 30

    build-windows-arm64:
        name: Build Windows ARM64 Package
        runs-on: ubuntu-latest
        if: ${{ github.event.inputs.packages == '' || github.event.inputs.packages == 'all' || github.event.inputs.packages == 'windows-only' }}

        steps:
            - name: Checkout repository
              uses: actions/checkout@v4

            - name: Create NuGet package
              working-directory: CefRuntime
              run: |
                  dotnet pack CefRuntime.csproj --runtime win-arm64 /p:CefBuildVersion=${{ github.event.inputs.cefbuildversion }}

            - name: Verify NuGet package contents
              run: |
                  echo "=== NuGet Package Contents ==="
                  unzip -l LocalPackages/*.nupkg | head -50
                  echo ""
                  echo "Checking for libcef.dll in package..."
                  unzip -l LocalPackages/*.nupkg | grep libcef.dll
                  echo "Checking the package is not empty..."
                  SIZE=$(stat -c%s LocalPackages/cef.runtime.win-arm64.*.nupkg)
                  echo "Package size: $((SIZE / 1048576)) MB"
                  test "$SIZE" -gt 104857600

            - name: Upload artifact
              uses: actions/upload-artifact@v4
              with:
                  name: cef.runtime.win-arm64
                  path: LocalPackages/*.nupkg
                  retention-days: 30
```

There is no separate "Download CEF runtime" step as the Linux and macOS jobs have: those call
their bash script explicitly, while `PrepareRedist` invokes the PowerShell script through
`pwsh`, which is preinstalled on `ubuntu-latest`. Packing is RID-agnostic, so a Linux runner
produces the same nupkg a Windows runner would.

- [ ] **Step 4: Add the new jobs to the summary gate**

Replace:

```yaml
        needs:
            [
                build-linux-x64,
                build-linux-arm64,
                build-macos-x64,
                build-macos-arm64,
            ]
```

with:

```yaml
        needs:
            [
                build-linux-x64,
                build-linux-arm64,
                build-macos-x64,
                build-macos-arm64,
                build-windows-x64,
                build-windows-arm64,
            ]
```

- [ ] **Step 5: Verify the YAML parses and endings held**

Run:
```bash
python -c "
import sys
try:
    import yaml
except ImportError:
    sys.exit('PyYAML missing; skip parse check')
d=yaml.safe_load(open('.github/workflows/build-cef-packages.yml',encoding='utf-8-sig'))
jobs=list(d['jobs']); print('jobs:', jobs)
assert 'build-windows-x64' in jobs and 'build-windows-arm64' in jobs
print('choices:', d['on']['workflow_dispatch']['inputs']['packages']['options'])
print('summary needs:', d['jobs']['summary']['needs'])
"
python -c "b=open('.github/workflows/build-cef-packages.yml','rb').read(); print('crlf' if b'\r\n' in b else 'lf')"
git diff --stat .github/workflows/build-cef-packages.yml
```
Expected: six build jobs plus `summary`, `windows-only` among the choices, both Windows jobs in
`summary.needs`, the file still `crlf`, and a diff of roughly 70–80 added lines rather than a
whole-file rewrite. If PyYAML is absent, skip the parse check and verify by reading the diff.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/build-cef-packages.yml
git commit -m "ci(cef): build the Windows redist packages

Add build-windows-x64 and build-windows-arm64 on ubuntu-latest (packing is
RID-agnostic and PrepareRedist drives the staging script through pwsh), a
windows-only input choice, and a >100 MB size assertion that catches the empty
package failure mode. The per-platform if: conditions become explicit, because
the previous negative form picks the wrong jobs with three platforms."
```

---

## Task 8: Documentation

**Files:**
- Modify: `CLAUDE.md` (LF)
- Modify: `UPGRADE.md` (LF)
- Modify: `README.md` (CRLF — see conventions)

- [ ] **Step 1: Rewrite the obsolete upgrade gate in `CLAUDE.md`**

Replace the whole `### 1. Check the official Windows nuget package FIRST — it gates the whole upgrade`
section — heading, prose, the `curl` block and the paragraph after it — with:

```markdown
### 1. Check the CEF CDN — it is the only external gate

All runtime packages are fork-built (`cef.runtime.{win,linux,osx}-*`), so no third-party
nuget package paces an upgrade any more. The only requirement is that the CDN has the
needed architectures for the version you want:

```bash
curl -s https://cef-builds.spotifycdn.com/index.json
```

Confirm a build exists for `windows64`, `windowsarm64`, `linux64`, `linuxarm64`, `macosx64`
and `macosarm64` before starting. Windows packages were fork-built from CEF 154.0.33 on;
before that the repo depended on `chromiumembeddedframework.runtime*`, which lagged CEF
stable by days and only published some patches per major.
```

- [ ] **Step 2: Note the Windows packages in the `CLAUDE.md` build section**

Replace:

```markdown
- **`CefGlue` (core), `CefGlue.Common.Shared`, `CefGlue.BrowserProcess.Core`
  build without the redist packages.** Everything downstream of `CefGlue.Common`
  (Avalonia/WPF/Demos/Tests) can only *restore* once the `cef.runtime.*` packages
  for the current version exist (see below).
```

with:

```markdown
- **`CefGlue` (core), `CefGlue.Common.Shared`, `CefGlue.BrowserProcess.Core`
  build without the redist packages.** Everything downstream of `CefGlue.Common`
  (Avalonia/WPF/Demos/Tests) can only *restore* once the `cef.runtime.*` packages
  for the current version exist (see below) — **including the Windows ones**
  (`cef.runtime.win-x64`/`win-arm64`), which this fork builds itself as of CEF 154.0.33.
```

- [ ] **Step 3: Extend the `CLAUDE.md` redist section with the Windows RIDs**

In `### 5. Redist packages (cef.runtime.*) — fork-custom, built by CI or locally`, replace:

```markdown
The Linux/macOS runtimes are **not on nuget** as official packages; this fork
builds them. Normally CI (`.github/workflows/build-cef-packages.yml`) produces
them. To build locally on Windows (uses **WSL**):
```

with:

```markdown
The Windows/Linux/macOS runtimes are all fork-built. Normally CI
(`.github/workflows/build-cef-packages.yml`) produces them. The **Windows** RIDs need no
WSL — `make_cefredist_windows.ps1` runs under `powershell` locally and `pwsh` on CI:

```bash
# from CefRuntime/
dotnet pack CefRuntime.csproj --runtime win-x64   "/p:CefBuildVersion=<full+version>" -c Release
dotnet pack CefRuntime.csproj --runtime win-arm64 "/p:CefBuildVersion=<full+version>" -c Release
```

The Linux/macOS RIDs still use the bash scripts, which on Windows go through **WSL**:
```

- [ ] **Step 4: Rewrite `UPGRADE.md` Step 1**

In `### Step 1: Determine the New CEF Version`, replace item 2:

```markdown
2. Find the desired CEF release (e.g., "Current Stable Build"). Be sure there is a package with the same version available for Windows [https://www.nuget.org/packages/chromiumembeddedframework.runtime](https://www.nuget.org/packages/chromiumembeddedframework.runtime).
```

with:

```markdown
2. Find the desired CEF release (e.g., "Current Stable Build"). Confirm the CDN has builds for every architecture this fork ships: `windows64`, `windowsarm64`, `linux64`, `linuxarm64`, `macosx64`, `macosarm64`. Since CEF 154.0.33 all runtime packages are fork-built, so no third-party nuget package gates the upgrade.
```

- [ ] **Step 5: Add the Windows pack commands to `UPGRADE.md` Step 6**

In `#### Option B: Building locally`, after the `cd CefRuntime` line and before the
`# Linux x64` block, insert:

```markdown
# Windows x64 / ARM64 (no WSL needed; staged by make_cefredist_windows.ps1)
dotnet pack CefRuntime.csproj --runtime win-x64 /p:CefBuildVersion=<FULL_BUILD_STRING>
dotnet pack CefRuntime.csproj --runtime win-arm64 /p:CefBuildVersion=<FULL_BUILD_STRING>

```

- [ ] **Step 6: Update the `UPGRADE.md` reference tables**

In the "Version Mapping Reference" table, replace the redist row:

```markdown
| Redist version (`CefRuntimePackageVersion`, `cef.runtime.*`) | `CEF_MAJOR.CEF_MINOR.CEF_PATCH` (= the CEF version) | `144.0.13` |
```

with:

```markdown
| Redist version (`CefRuntimePackageVersion`, `cef.runtime.*`, all six RIDs incl. `win-x64`/`win-arm64`) | `CEF_MAJOR.CEF_MINOR.CEF_PATCH` (= the CEF version) | `144.0.13` |
```

and in the "Files Modified During an Upgrade" table, after the
`CefRuntime/make_cefredist_osx.sh` row, add:

```markdown
| `CefRuntime/make_cefredist_windows.ps1` | Download URL | **Auto** (reads from `cef-version.json`) |
```

- [ ] **Step 7: Update the `README.md` package table and surrounding prose**

In the "Why This Fork?" table, after the header separator row and before the
`cef.runtime.linux-x64` row, add:

```markdown
| [cef.runtime.win-x64](https://www.nuget.org/packages/cef.runtime.win-x64) | [![NuGet](https://img.shields.io/nuget/v/cef.runtime.win-x64?logo=nuget)](https://www.nuget.org/packages/cef.runtime.win-x64) | Windows x64 |
| [cef.runtime.win-arm64](https://www.nuget.org/packages/cef.runtime.win-arm64) | [![NuGet](https://img.shields.io/nuget/v/cef.runtime.win-arm64?logo=nuget)](https://www.nuget.org/packages/cef.runtime.win-arm64) | Windows ARM64 |
```

Then replace the sentence after the table:

```markdown
The source projects for these packages are also included directly in this workspace, so you can build them locally if needed. (Windows uses the official `chromiumembeddedframework.runtime.*` packages from nuget.org.)
```

with:

```markdown
The source projects for these packages are also included directly in this workspace, so you can build them locally if needed. As of CEF 154.0.33 the Windows runtimes are fork-built too, so no third-party package paces a release.
```

- [ ] **Step 8: Fix the remaining "four packages" claims in `README.md`**

Run `grep -n "four runtime packages\|official" README.md` and update the two places the spec
calls out: the restore note (around line 123) and the build note (around line 142). Replace
`All four runtime packages` with `All six runtime packages`, and drop any clause saying the
official `chromiumembeddedframework` packages are required for Windows. Quote the exact lines
in the commit if they differ from this wording.

- [ ] **Step 9: Verify docs**

Run:
```bash
grep -rn "chromiumembeddedframework" README.md CLAUDE.md UPGRADE.md || echo "no stale upstream references: OK"
python -c "
for f in ['README.md','CLAUDE.md','UPGRADE.md']:
    b=open(f,'rb').read(); print(f, 'crlf' if b'\r\n' in b else 'lf')"
git diff --stat README.md CLAUDE.md UPGRADE.md
```
Expected: the only surviving `chromiumembeddedframework` mentions are historical notes that
explicitly say the dependency was dropped (the `CLAUDE.md` gate rewrite keeps one); `README.md`
still `crlf`, the other two `lf`; and no whole-file churn.

- [ ] **Step 10: Commit**

```bash
git add README.md CLAUDE.md UPGRADE.md
git commit -m "docs: Windows runtime packages are fork-built

The official Windows nuget package no longer gates an upgrade, so rewrite that
guidance in CLAUDE.md and UPGRADE.md around CDN availability, add the Windows
pack commands, and list cef.runtime.win-x64/win-arm64 in the README."
```

---

## Task 9: Final sweep

**Files:**
- Modify: `docs/superpowers/specs/2026-10-05-windows-cef-runtime-packages-design.md` (status line)

- [ ] **Step 1: Clean restore, build and test**

Run:
```powershell
dotnet restore Xilium.CefGlue.slnx 2>&1 | Select-String -Pattern "error|NU11" | Select-Object -First 5
dotnet build Xilium.CefGlue.slnx -c Release --no-restore 2>&1 | Select-String -Pattern "Fehler|error" | Select-Object -Last 5
dotnet test CefGlue.Tests/CefGlue.Tests.csproj -c Release --no-build 2>&1 | Select-String -Pattern "Bestanden!|Fehler!" | Select-Object -Last 3
```
Expected: no restore errors, 0 build errors, `gesamt: 143` with 142 passed and 1 skipped.

- [ ] **Step 2: Verify the tree is clean apart from known untracked files**

Run:
```bash
git status --short
```
Expected: only `?? bash.exe.stackdump` (pre-existing). `CefRuntime/redist/` and
`LocalPackages/` must not appear; if they do, they are not ignored and that must be fixed
before pushing.

- [ ] **Step 3: Mark the spec implemented**

In the spec, replace:

```markdown
Status: approved design, not yet implemented
```

with:

```markdown
Status: implemented on `build/cef-154.0.33`
```

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-10-05-windows-cef-runtime-packages-design.md
git commit -m "docs: mark the Windows runtime package design implemented"
```

- [ ] **Step 5: Report**

Summarise for the user: measured package sizes, the test result, that publish and the no-RID
fallback were verified, and the unchanged follow-ups (arm64 verified by inspection only, the
Linux/macOS publish gap, `win-x86` deferred, and that CI must run the `build-cef-packages`
workflow to publish the new packages before the managed packages can ship).

---

## Self-review

**Spec coverage:** staging script → Task 1; csproj RIDs → Task 2; props with build+publish and
`$(Platform)` fallback → Task 3; payload filter and size/content expectations → Tasks 1 and 4;
repo wiring incl. removing the `CefGlue.Common.targets` target → Task 5; verification steps 1–6
of the spec → Tasks 4, 5 and 6; CI jobs, `windows-only`, size assertion → Task 7; docs → Task 8;
known limits restated → Task 9 step 5. No spec section is unimplemented.

**Naming consistency:** the script is `make_cefredist_windows.ps1` everywhere; props files are
`cef.runtime.win-x64.props`/`cef.runtime.win-arm64.props`; targets are
`CefRedistWinX64CopyResources`, `CefRedistWinX64CopyPublishResources`,
`CefRedistWinArm64CopyResources`, `CefRedistWinArm64CopyPublishResources`; override properties
are `CefRedistWinX64TargetDir`/`CefRedistWinX64PublishDir` and the `WinArm64` pair; the MSBuild
property gating the script choice is `IsWindowsRedist` in both the task and the verification
step.

**Known plan risk:** Task 2 step 1 expects a specific failure whose exact text depends on the
SDK; treat any non-zero exit as the expected starting state rather than matching the message.
