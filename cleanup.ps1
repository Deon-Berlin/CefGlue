# Cleanup script for CefGlue repository
# Removes bin/obj folders and downloaded CEF archives

$ErrorActionPreference = "Continue"
$rootPath = $PSScriptRoot

Write-Host "Cleaning up CefGlue repository..." -ForegroundColor Cyan
Write-Host "Root path: $rootPath" -ForegroundColor Gray

# Remove bin and obj folders
Write-Host "`nRemoving bin and obj folders..." -ForegroundColor Yellow
$foldersToRemove = Get-ChildItem -Path $rootPath -Include "bin", "obj" -Recurse -Directory -Force
$folderCount = 0

foreach ($folder in $foldersToRemove) {
	Write-Host "  Removing: $($folder.FullName)" -ForegroundColor DarkGray
	Remove-Item -Path $folder.FullName -Recurse -Force -ErrorAction SilentlyContinue
	$folderCount++
}

Write-Host "Removed $folderCount bin/obj folders." -ForegroundColor Green

# Remove downloaded .tar.bz2 files
Write-Host "`nRemoving downloaded .tar.bz2 files..." -ForegroundColor Yellow
$archivesToRemove = Get-ChildItem -Path $rootPath -Filter "*.tar.bz2" -Recurse -File -Force
$archiveCount = 0

foreach ($archive in $archivesToRemove) {
	Write-Host "  Removing: $($archive.FullName)" -ForegroundColor DarkGray
	Remove-Item -Path $archive.FullName -Force -ErrorAction SilentlyContinue
	$archiveCount++
}

Write-Host "Removed $archiveCount .tar.bz2 files." -ForegroundColor Green

# Remove tmp folders from cef.redist projects
Write-Host "`nRemoving tmp folders from cef.redist projects..." -ForegroundColor Yellow
$tmpFolders = Get-ChildItem -Path $rootPath -Include "tmp-*" -Recurse -Directory -Force
$tmpCount = 0

foreach ($folder in $tmpFolders) {
	Write-Host "  Removing: $($folder.FullName)" -ForegroundColor DarkGray
	Remove-Item -Path $folder.FullName -Recurse -Force -ErrorAction SilentlyContinue
	$tmpCount++
}

Write-Host "Removed $tmpCount tmp folders." -ForegroundColor Green

# Remove packages folder in CefGlue
$packagesFolder = Join-Path $rootPath "CefGlue\packages"
if (Test-Path $packagesFolder) {
	Write-Host "`nRemoving CefGlue packages folder..." -ForegroundColor Yellow
	Write-Host "  Removing: $packagesFolder" -ForegroundColor DarkGray
	Remove-Item -Path $packagesFolder -Recurse -Force -ErrorAction SilentlyContinue
	Write-Host "Removed packages folder." -ForegroundColor Green
}

# Remove LocalPackages folder
$localPackagesFolder = Join-Path $rootPath "LocalPackages"
if (Test-Path $localPackagesFolder) {
	Write-Host "`nRemoving LocalPackages folder..." -ForegroundColor Yellow
	Write-Host "  Removing: $localPackagesFolder" -ForegroundColor DarkGray
	Remove-Item -Path $localPackagesFolder -Recurse -Force -ErrorAction SilentlyContinue
	Write-Host "Removed LocalPackages folder." -ForegroundColor Green
}

Write-Host "`nCleanup complete!" -ForegroundColor Cyan
