#Requires -Version 5.1
<#
.SYNOPSIS
    Upgrades CefGlue to a new CEF version.

.DESCRIPTION
    Automates the CEF version upgrade process:
      - Parses the version string into its components
      - Updates cef-version.json
      - Updates the default cefbuildversion in the GitHub Actions workflow
      - Cleans up old build artefacts
      - Downloads the new CEF C API headers
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

# ── Step 3: Update GitHub Actions workflow ────────────────────────────────────
$workflowFile = Join-Path $ScriptDir '.github\workflows\build-cef-packages.yml'

if (Test-Path $workflowFile) {
    Write-Step "Updating .github/workflows/build-cef-packages.yml"

    $content = [System.IO.File]::ReadAllText($workflowFile)

    # Replace the default: "..." value that holds a CEF build version string.
    # The pattern is specific: the existing value contains +g<hash>+chromium-
    $updated = [regex]::Replace(
        $content,
        '(default:\s*")[^"]*\+g[0-9a-f]+\+chromium-[^"]*(")',
        "`${1}${CefBuildVersion}`${2}"
    )

    if ($updated -eq $content) {
        Write-Warn "Could not find the cefbuildversion default value in the workflow file — skipping"
    } else {
        [System.IO.File]::WriteAllText($workflowFile, $updated, [System.Text.UTF8Encoding]::new($false))
        Write-Ok ".github/workflows/build-cef-packages.yml updated"
    }
} else {
    Write-Warn ".github/workflows/build-cef-packages.yml not found — skipping"
}

# ── Step 5: Download CEF C API headers ───────────────────────────────────────
if (-not $SkipDownload) {
    Write-Step "Downloading CEF C API headers"

    $encodedVersion = $CefBuildVersion.Replace('+', '%2B')
    $downloadUrl    = "https://cef-builds.spotifycdn.com/cef_binary_${encodedVersion}_linux64_minimal.tar.bz2"
    $tempDir        = Join-Path $ScriptDir '.upgrade-cef'
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    $archivePath    = Join-Path $tempDir 'cef.tar.bz2'

    Write-Info "URL: $downloadUrl"

    try {
        # Use curl.exe if available (faster, shows progress); fall back to Invoke-WebRequest
        if (Get-Command 'curl.exe' -ErrorAction SilentlyContinue) {
            & curl.exe -L --fail --progress-bar -o $archivePath $downloadUrl
            if ($LASTEXITCODE -ne 0) { throw "curl.exe failed with exit code $LASTEXITCODE" }
        } else {
            Write-Info "curl.exe not found — using Invoke-WebRequest (no progress bar)"
            Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing
        }

        Write-Step "Extracting headers"
        $extractDir = Join-Path $tempDir 'cef_extract'
        New-Item -ItemType Directory -Path $extractDir | Out-Null

        # Use Python's built-in tarfile module — avoids dependency on an external bzip2 binary
        # which Windows tar.exe (libarchive) requires but does not ship with.
        & python -c @"
import tarfile, sys
with tarfile.open(sys.argv[1], 'r:bz2') as t:
    t.extractall(sys.argv[2])
"@ $archivePath $extractDir
        if ($LASTEXITCODE -ne 0) { throw "Extraction failed with exit code $LASTEXITCODE" }

        $includeDest = Join-Path $ScriptDir 'CefGlue.Interop.Gen\include'
        if (-not (Test-Path $includeDest)) { New-Item -ItemType Directory -Path $includeDest | Out-Null }

        # Copy all files from the extracted include/ directory
        $extractedInclude = Get-ChildItem -Path $extractDir -Recurse -Filter 'include' -Directory |
            Select-Object -First 1

        if ($null -eq $extractedInclude) {
            throw "Could not find 'include' directory in the extracted archive"
        }

        Copy-Item -Path (Join-Path $extractedInclude.FullName '*') -Destination $includeDest -Recurse -Force
        Write-Ok "CEF headers installed to CefGlue.Interop.Gen/include/"
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
Write-Host "  v .github/workflows/build-cef-packages.yml updated"
if (-not $SkipDownload)  { Write-Host "  v CEF C API headers downloaded" }
if (-not $SkipInterop)   { Write-Host "  v Interop bindings regenerated" }
if ($Build)              { Write-Host "  v Solution built" }
Write-Host ""
Write-Host "Manual steps still required:" -ForegroundColor Yellow
Write-Host "  1. Fix any API breaking changes in CefGlue source code"
Write-Host "     > dotnet build Xilium.CefGlue.slnx -c Release"
Write-Host "  2. Build CEF redistribution packages:"
Write-Host "     > .\build-local-packages.ps1"
Write-Host "     > or: cd CefRuntime; dotnet pack CefRuntime.csproj --runtime linux-x64 /p:CefBuildVersion=$CefBuildVersion"
Write-Host "  3. Run tests:"
Write-Host "     > dotnet test CefGlue.Tests\CefGlue.Tests.csproj -c Release"
Write-Host "  4. Update README.md with new version information"
Write-Host "  5. Commit changes:"
Write-Host "     > git add -A; git commit -m 'Upgrade CEF to $CefVersion'"
Write-Host ""
