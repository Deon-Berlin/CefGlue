# README

This is a repackaging fork of the Chromium Embedded Framework (CEF) 
binary distribution files for Linux, found at 
https://cef-builds.spotifycdn.com/index.html, into a NuGet package.

Can use Aria2 for faster downloads if found but CEF is sometimes 
half a gigabyte big so it might take a while.

These utilites are required:

 - Aria2 or cURL
 - tar (with bzip2 support)
 - sh
 - binutils (for command `strip`)
 - aarch64-linux-gnu-strip (optional: for ARM64 packages)
 - nuget.exe (use with Wine if on Linux, `dotnet` currently does not support additional properties)
 
Usage: `./make_cefredist.sh [linux64 or linuxarm64]`

To pack into NUPKG: 
`nuget.exe pack cef.redist.linux.nuspec -P version=<CEF version here>;id=<linux64 or linuxarm64>`
