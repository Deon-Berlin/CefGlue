#Requires -Version 5.1
<#
.SYNOPSIS
    Upgrades CefGlue to a new CEF version.

.DESCRIPTION
    Automates the CEF version upgrade process:
      - Parses the version string into its components
      - Updates cef-version.json
      - Sets CefRuntimePackageVersion in CefVersion.props to the CEF version
      - Updates the default CEF version in the build-cef-packages workflow
      - Warns if chromiumembeddedframework.runtime is not on nuget.org yet
      - Downloads the new CEF C API headers (linux64 + windows64)
      - Regenerates the interop bindings
      - Optionally builds the solution

.PARAMETER CefBuildVersion
    Full CEF build version string.
    Example: 144.0.13+g9f739aa+chromium-144.0.7559.133

.PARAMETER SkipDownload
    Skip downloading the CEF C API headers.

.PARAMETER SkipInterop
    Skip regenerating the interop bindings.

.PARAMETER Build
    Build the solution after updating.

.EXAMPLE
    .\upgrade-cef.ps1 144.0.13+g9f739aa+chromium-144.0.7559.133
    .\upgrade-cef.ps1 144.0.13+g9f739aa+chromium-144.0.7559.133 -Build
    .\upgrade-cef.ps1 144.0.13+g9f739aa+chromium-144.0.7559.133 -SkipDownload -SkipInterop
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0,
        HelpMessage = "Full CEF build version string, e.g. 144.0.13+g9f739aa+chromium-144.0.7559.133")]
    [string]$CefBuildVersion,

    [switch]$SkipDownload,
    [switch]$SkipInterop,
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot

# ── Helpers ───────────────────────────────────────────────────────────────────
function Write-Step  ([string]$msg) { Write-Host "`n==> $msg" -ForegroundColor Blue }
function Write-Ok    ([string]$msg) { Write-Host "  v $msg"   -ForegroundColor Green }
function Write-Info  ([string]$msg) { Write-Host "  · $msg"   -ForegroundColor Cyan }
function Write-Warn  ([string]$msg) { Write-Host "  ! $msg"   -ForegroundColor Yellow }

# ── Validate version string ───────────────────────────────────────────────────
$versionPattern = '^(\d+\.\d+\.\d+)\+(g[0-9a-f]+)\+chromium-(\d+\.\d+\.\d+\.\d+)$'
if ($CefBuildVersion -notmatch $versionPattern) {
    Write-Error "Invalid CEF build version format: $CefBuildVersion`nExpected: MAJOR.MINOR.PATCH+gHASH+chromium-MAJOR.MINOR.BUILD.PATCH"
    exit 1
}

$CefVersion      = $Matches[1]
$CefGitHash      = $Matches[2]
$ChromiumVersion = $Matches[3]

# CefGlue (managed CefGlue.Next.*) version — SemVer 2.0.0, 3-part:
#   base release  = CEF_MAJOR.CHROME_BUILD.CHROME_PATCH               (e.g. 149.7827.201)
#   patch rebuild = CEF_MAJOR.CHROME_BUILD.(CHROME_PATCH * 10 + N)    (e.g. 149.7827.2011)
# A fresh CEF upgrade is always a base release (patch 0, trailing 0 stripped). To republish
# the SAME CEF binaries with a fix (NuGet versions are immutable), bump cefglue_version in
# cef-version.json to the *10+N patch form by hand, and bump CefRuntimePackageVersion in
# CefVersion.props to the matching redist patch (e.g. 149.0.61). See UPGRADE.md.
$cefMajor        = $CefVersion.Split('.')[0]
$chromeParts     = $ChromiumVersion.Split('.')
$CefGlueVersion  = "$cefMajor.$($chromeParts[2]).$($chromeParts[3])"

# ── Banner ────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "CEF Version Upgrade" -ForegroundColor White
Write-Host "----------------------------------------"
Write-Info "Build version:    $CefBuildVersion"
Write-Info "CEF version:      $CefVersion"
Write-Info "Git hash:         $CefGitHash"
Write-Info "Chromium version: $ChromiumVersion"
Write-Info "CefGlue version:  $CefGlueVersion"
Write-Host ""

# ── Step 2: Update cef-version.json ──────────────────────────────────────────
Write-Step "Updating cef-version.json"

$jsonContent = @"
{
  "cef_version": "$CefVersion",
  "cef_build_version": "$CefBuildVersion",
  "chromium_version": "$ChromiumVersion",
  "cefglue_version": "$CefGlueVersion",
  "cef_git_hash": "$CefGitHash"
}
"@

# Write without BOM so the file stays plain UTF-8
[System.IO.File]::WriteAllText(
    [System.IO.Path]::Combine($ScriptDir, 'cef-version.json'),
    $jsonContent,
    [System.Text.UTF8Encoding]::new($false)
)
Write-Ok "cef-version.json updated"

# ── Step 3: Update the redist (cef.runtime.*) package version ───────────────
# A fresh upgrade is a base release, so the redist version is the CEF version.
Write-Step "Updating CefRuntimePackageVersion in CefVersion.props"

$propsFile = Join-Path $ScriptDir 'CefVersion.props'
$propsContent = [System.IO.File]::ReadAllText($propsFile)
$propsPattern = '(<CefRuntimePackageVersion>)[^<]*(</CefRuntimePackageVersion>)'
if ($propsContent -notmatch $propsPattern) {
    throw "CefRuntimePackageVersion not found in CefVersion.props"
}
$propsUpdated = [regex]::Replace($propsContent, $propsPattern, "`${1}${CefVersion}`${2}")
[System.IO.File]::WriteAllText($propsFile, $propsUpdated, [System.Text.UTF8Encoding]::new($false))
Write-Ok "CefRuntimePackageVersion set to $CefVersion"

# ── Step 3b: Update the redist workflow's default CEF version ────────────────
# The workflow_dispatch input defaults to the current build string (push events
# read cef-version.json instead). Replace every build-version string in the file
# (the description example and the default); line endings are left untouched.
Write-Step "Updating the default CEF version in build-cef-packages.yml"

$workflowFile  = Join-Path $ScriptDir '.github\workflows\build-cef-packages.yml'
$versionRegex  = '\d+\.\d+\.\d+\+g[0-9a-f]+\+chromium-[\d.]+\d'
$workflowText  = if (Test-Path $workflowFile) { [System.IO.File]::ReadAllText($workflowFile) } else { '' }
if ($workflowText -match $versionRegex) {
    $workflowText = [regex]::Replace($workflowText, $versionRegex, { param($m) $CefBuildVersion })
    [System.IO.File]::WriteAllText($workflowFile, $workflowText, [System.Text.UTF8Encoding]::new($false))
    Write-Ok "build-cef-packages.yml default set to $CefBuildVersion"
} else {
    Write-Warn "No CEF build version found in build-cef-packages.yml; update its default by hand"
}

# ── Step 4: Check the official Windows runtime package ────────────────────────
# Directory.Packages.props pins chromiumembeddedframework.runtime at $(CefVersion)
# unconditionally, so restore fails with NU1102 on every platform until it exists.
Write-Step "Checking nuget.org for chromiumembeddedframework.runtime $CefVersion"

try {
    $nugetIndex = Invoke-RestMethod -Uri 'https://api.nuget.org/v3-flatcontainer/chromiumembeddedframework.runtime/index.json' -UseBasicParsing
    if ($nugetIndex.versions -contains $CefVersion) {
        Write-Ok "chromiumembeddedframework.runtime $CefVersion is published"
    } else {
        Write-Warn "chromiumembeddedframework.runtime $CefVersion is NOT published (latest: $($nugetIndex.versions[-1]))"
        Write-Warn "dotnet restore will fail with NU1102 on every platform until it is"
    }
} catch {
    Write-Warn "Could not reach nuget.org; skipping the Windows package check"
}

# ── Step 5: Download CEF C API headers ───────────────────────────────────────
# Overlay linux64 then windows64 (do not mirror-delete): the Windows-specific
# headers (cef_sandbox_win.h, internal/cef_*_win.h, wrapper/cef_library_loader.h)
# ship only in the windows package.
if (-not $SkipDownload) {
    $encodedVersion = $CefBuildVersion.Replace('+', '%2B')
    $tempDir        = Join-Path $ScriptDir '.upgrade-cef'
    $includeDest    = Join-Path $ScriptDir 'CefGlue.Interop.Gen\include'
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    if (-not (Test-Path $includeDest)) { New-Item -ItemType Directory -Path $includeDest | Out-Null }

    try {
        foreach ($platform in @('linux64', 'windows64')) {
            Write-Step "Downloading CEF C API headers ($platform)"

            $downloadUrl = "https://cef-builds.spotifycdn.com/cef_binary_${encodedVersion}_${platform}_minimal.tar.bz2"
            $archivePath = Join-Path $tempDir "$platform.tar.bz2"
            $extractDir  = Join-Path $tempDir $platform
            Write-Info "URL: $downloadUrl"

            # Use curl.exe if available (faster, shows progress); fall back to Invoke-WebRequest
            if (Get-Command 'curl.exe' -ErrorAction SilentlyContinue) {
                & curl.exe -L --fail --progress-bar -o $archivePath $downloadUrl
                if ($LASTEXITCODE -ne 0) { throw "curl.exe failed with exit code $LASTEXITCODE" }
            } else {
                Write-Info "curl.exe not found — using Invoke-WebRequest (no progress bar)"
                Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing
            }

            # Use Python's built-in tarfile module — avoids dependency on an external bzip2 binary
            # which Windows tar.exe (libarchive) requires but does not ship with.
            # Only the include/ tree is extracted; the binaries are not needed here.
            New-Item -ItemType Directory -Path $extractDir | Out-Null
            & python -c @"
import tarfile, sys
with tarfile.open(sys.argv[1], 'r:bz2') as t:
    t.extractall(sys.argv[2], members=[m for m in t.getmembers() if m.name.split('/')[1:2] == ['include']])
"@ $archivePath $extractDir
            if ($LASTEXITCODE -ne 0) { throw "Extraction failed with exit code $LASTEXITCODE" }

            $topDir = Get-ChildItem -Path $extractDir -Directory | Select-Object -First 1
            $extractedInclude = if ($null -ne $topDir) { Join-Path $topDir.FullName 'include' } else { $null }
            if ($null -eq $extractedInclude -or -not (Test-Path $extractedInclude)) {
                throw "Could not find 'include' directory in the $platform archive"
            }

            Copy-Item -Path (Join-Path $extractedInclude '*') -Destination $includeDest -Recurse -Force
            Write-Ok "$platform headers installed to CefGlue.Interop.Gen/include/"
        }
    } finally {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Warn "Skipping header download (-SkipDownload)"
}

# ── Step 6: Regenerate interop bindings ───────────────────────────────────────
if (-not $SkipInterop) {
    Write-Step "Regenerating interop bindings"

    $interopDir   = Join-Path $ScriptDir 'CefGlue.Interop.Gen'
    $interopScript = Join-Path $interopDir 'cefglue_interop_gen.py'

    if (Test-Path $interopScript) {
        Push-Location $interopDir
        try {
            & python -B cefglue_interop_gen.py `
                --cpp-header-dir include `
                --cefglue-dir ..\CefGlue\ `
                --no-backup
            if ($LASTEXITCODE -ne 0) { throw "Interop generator failed with exit code $LASTEXITCODE" }
        } finally {
            Pop-Location
        }
        Write-Ok "Interop bindings regenerated"
    } else {
        Write-Warn "cefglue_interop_gen.py not found in $interopDir — skipping"
    }
} else {
    Write-Warn "Skipping interop regeneration (-SkipInterop)"
}

# ── Step 7/10: Build the solution ─────────────────────────────────────────────
if ($Build) {
    Write-Step "Building solution"
    Push-Location $ScriptDir
    try {
        & dotnet build Xilium.CefGlue.slnx -c Release
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
    } finally {
        Pop-Location
    }
    Write-Ok "Solution built successfully"
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "========================================" -ForegroundColor White
Write-Host " Automated upgrade steps complete"       -ForegroundColor White
Write-Host "========================================" -ForegroundColor White
Write-Host ""
Write-Host "Completed:" -ForegroundColor Green
Write-Host "  v cef-version.json updated"
Write-Host "  v CefRuntimePackageVersion set to $CefVersion"
Write-Host "  v build-cef-packages.yml default CEF version updated"
if (-not $SkipDownload)  { Write-Host "  v CEF C API headers downloaded (linux64 + windows64)" }
if (-not $SkipInterop)   { Write-Host "  v Interop bindings regenerated" }
if ($Build)              { Write-Host "  v Solution built" }
Write-Host ""
Write-Host "Manual steps still required:" -ForegroundColor Yellow
Write-Host "  1. Review the generated diff: a clean upgrade usually changes only version.g.cs;"
Write-Host "     revert whitespace-only or path-separator-only churn in other generated files"
Write-Host "     and keep each generated file's line endings as in git (version.g.cs is CRLF)"
Write-Host "  2. Fix any API breaking changes in CefGlue source code"
Write-Host "     > dotnet build Xilium.CefGlue.slnx -c Release"
Write-Host "  3. Build the CEF redistribution packages (cef.runtime.*):"
Write-Host "     > run the build-cef-packages workflow, or locally per RID:"
Write-Host "     > cd CefRuntime; dotnet pack CefRuntime.csproj --runtime <rid> `"/p:CefBuildVersion=$CefBuildVersion`" -c Release"
Write-Host "  4. Run tests:"
Write-Host "     > dotnet test CefGlue.Tests\CefGlue.Tests.csproj -c Release"
Write-Host "  5. Update README.md with new version information"
Write-Host "  6. Commit changes:"
Write-Host "     > git add -A; git commit -m 'build(cef): upgrade to $CefVersion'"
Write-Host ""
