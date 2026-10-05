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

$OnWindows = $env:OS -eq 'Windows_NT'
# Tool names differ by host: Windows resolves the bundled curl.exe/tar.exe, while the CI
# runners (where this also runs, because packing is RID-agnostic) have plain curl/tar.
$TarExe = 'tar'
$CurlExe = 'curl'
if ($OnWindows) {
    $TarExe = 'tar.exe'
    $CurlExe = 'curl.exe'
}

# Windows ships bsdtar (3.5.2) whose build advertises zlib only: handed a .tar.bz2 it hangs
# indefinitely at 0% CPU instead of failing. So on Windows we decompress with bzip2 first
# and give tar a plain .tar, which it unpacks fine (and fast). bzip2.exe comes with Git for
# Windows, already a prerequisite for this repo. Every other host has a tar that does bz2
# natively, so CI needs nothing extra.
function Resolve-Bzip2Path {
    $cmd = Get-Command bzip2 -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        (Join-Path "$env:ProgramFiles" 'Git\mingw64\bin\bzip2.exe'),
        (Join-Path "$([Environment]::GetEnvironmentVariable('ProgramFiles(x86)'))" 'Git\mingw64\bin\bzip2.exe'),
        (Join-Path "$env:LOCALAPPDATA" 'Programs\Git\mingw64\bin\bzip2.exe')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }

    throw "bzip2 not found. Windows' bundled tar cannot unpack .tar.bz2 (it hangs), so staging needs bzip2: install Git for Windows, which ships bzip2.exe, or put bzip2 on PATH."
}

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

    # A killed run can leave a truncated archive behind; anything far below the real
    # distribution size (~145-170 MB) is treated as incomplete and fetched again.
    if ((Test-Path $archive) -and ((Get-Item $archive).Length -lt 50MB)) {
        Write-Host 'Discarding incomplete download'
        Remove-Item -Force $archive
    }

    if (-not (Test-Path $archive)) {
        $encodedVersion = $CefBuildVersion.Replace('+', '%2B')
        $url = "https://cef-builds.spotifycdn.com/cef_binary_${encodedVersion}_${arch}_minimal.tar.bz2"
        Write-Host "Downloading CEF binaries v$CefVersion-$arch"
        & $CurlExe -fL --retry 3 -o $archive $url
        if ($LASTEXITCODE -ne 0) {
            Remove-Item -Force -ErrorAction SilentlyContinue $archive
            throw "Download failed for $url"
        }
    }
    else {
        Write-Host "CEF binaries v$CefVersion-$arch already downloaded"
    }

    # Judge "already extracted" by content, not by the directory existing: a killed run
    # leaves an empty directory behind, and testing only existence would skip extraction
    # and then fail later with a confusing "Release directory not found".
    $alreadyExtracted = $false
    if (Test-Path $binaries) {
        $alreadyExtracted = $null -ne (Get-ChildItem -Path $binaries -Directory -Recurse -Filter 'Release' | Select-Object -First 1)
        if (-not $alreadyExtracted) {
            Write-Host "Clearing incomplete extraction in $binaries"
            Remove-Item -Recurse -Force $binaries
        }
    }

    if (-not $alreadyExtracted) {
        Write-Host "Extracting CEF binaries v$CefVersion-$arch"
        New-Item -ItemType Directory -Force -Path $binaries | Out-Null

        if ($OnWindows) {
            $bzip2 = Resolve-Bzip2Path
            $tarFile = Join-Path $tmp "cef-$CefVersion.tar"

            # Always decompress fresh: a truncated .tar left by a killed run would
            # otherwise be reused and fail every time.
            Remove-Item -Force -ErrorAction SilentlyContinue $tarFile
            Write-Host "Decompressing with $bzip2"
            $bz = Start-Process -FilePath $bzip2 -ArgumentList '-dc', $archive `
                -RedirectStandardOutput $tarFile -NoNewWindow -PassThru -Wait
            if ($bz.ExitCode -ne 0) {
                Remove-Item -Force -ErrorAction SilentlyContinue $tarFile
                Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $binaries
                throw "bzip2 failed to decompress $archive (exit $($bz.ExitCode))"
            }

            & $TarExe -xf $tarFile -C $binaries
            $tarExit = $LASTEXITCODE
            Remove-Item -Force -ErrorAction SilentlyContinue $tarFile
            if ($tarExit -ne 0) {
                Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $binaries
                throw "Extraction failed for $tarFile (exit $tarExit)"
            }
        }
        else {
            & $TarExe -xjf $archive -C $binaries
            if ($LASTEXITCODE -ne 0) {
                Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $binaries
                throw "Extraction failed for $archive"
            }
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
