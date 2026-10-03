# CefGlue - CEF 154.0.33 Fork

This repository contains a fork of [CefGlue](https://github.com/OutSystems/CefGlue) updated to **CEF version 154.0.33** (Chromium 154.0.8037.94), along with the necessary CEF redistribution packages for Linux and macOS.

## Overview

CefGlue is a .NET binding for The Chromium Embedded Framework (CEF). It allows you to embed Chromium in .NET applications and provides both **Avalonia** and **WPF** web browser control implementations.

## NuGet Packages

| Package | Version | Description |
|---------|---------|-------------|
| [CefGlue.Next.Avalonia](https://www.nuget.org/packages/CefGlue.Next.Avalonia) | [![NuGet](https://img.shields.io/nuget/v/CefGlue.Next.Avalonia?logo=nuget)](https://www.nuget.org/packages/CefGlue.Next.Avalonia) | Avalonia browser control |
| [CefGlue.Next.WPF](https://www.nuget.org/packages/CefGlue.Next.WPF) | [![NuGet](https://img.shields.io/nuget/v/CefGlue.Next.WPF?logo=nuget)](https://www.nuget.org/packages/CefGlue.Next.WPF) | WPF browser control |
| [CefGlue.Next.Core](https://www.nuget.org/packages/CefGlue.Next.Core) | [![NuGet](https://img.shields.io/nuget/v/CefGlue.Next.Core?logo=nuget)](https://www.nuget.org/packages/CefGlue.Next.Core) | Core .NET binding for CEF |
| [CefGlue.Next.Common](https://www.nuget.org/packages/CefGlue.Next.Common) | [![NuGet](https://img.shields.io/nuget/v/CefGlue.Next.Common?logo=nuget)](https://www.nuget.org/packages/CefGlue.Next.Common) | Shared browser adapter |
| [CefGlue.Next.Common.Shared](https://www.nuget.org/packages/CefGlue.Next.Common.Shared) | [![NuGet](https://img.shields.io/nuget/v/CefGlue.Next.Common.Shared?logo=nuget)](https://www.nuget.org/packages/CefGlue.Next.Common.Shared) | Shared utilities and serialization |
| [CefGlue.Next.BrowserProcess.Core](https://www.nuget.org/packages/CefGlue.Next.BrowserProcess.Core) | [![NuGet](https://img.shields.io/nuget/v/CefGlue.Next.BrowserProcess.Core?logo=nuget)](https://www.nuget.org/packages/CefGlue.Next.BrowserProcess.Core) | Browser sub-process core |

> Most applications only need **CefGlue.Next.Avalonia** or **CefGlue.Next.WPF**; the remaining packages are pulled in transitively as dependencies.

### Why This Fork?

At the time of this fork, the official NuGet packages for the following CEF redistributables were not yet available for version 154.0.33, so this fork builds and publishes them:

| Package | Version | Platform |
|---------|---------|----------|
| [cef.runtime.linux-x64](https://www.nuget.org/packages/cef.runtime.linux-x64) | [![NuGet](https://img.shields.io/nuget/v/cef.runtime.linux-x64?logo=nuget)](https://www.nuget.org/packages/cef.runtime.linux-x64) | Linux x64 |
| [cef.runtime.linux-arm64](https://www.nuget.org/packages/cef.runtime.linux-arm64) | [![NuGet](https://img.shields.io/nuget/v/cef.runtime.linux-arm64?logo=nuget)](https://www.nuget.org/packages/cef.runtime.linux-arm64) | Linux ARM64 |
| [cef.runtime.osx-x64](https://www.nuget.org/packages/cef.runtime.osx-x64) | [![NuGet](https://img.shields.io/nuget/v/cef.runtime.osx-x64?logo=nuget)](https://www.nuget.org/packages/cef.runtime.osx-x64) | macOS x64 |
| [cef.runtime.osx-arm64](https://www.nuget.org/packages/cef.runtime.osx-arm64) | [![NuGet](https://img.shields.io/nuget/v/cef.runtime.osx-arm64?logo=nuget)](https://www.nuget.org/packages/cef.runtime.osx-arm64) | macOS ARM64 |

The source projects for these packages are also included directly in this workspace, so you can build them locally if needed. (Windows uses the official `chromiumembeddedframework.runtime.*` packages from nuget.org.)

## Repository Structure

The layout is flat: the projects and the solution sit directly in the repository root.

```
├── Xilium.CefGlue.slnx           # Solution
├── cef-version.json              # Central CEF version configuration (single source of truth)
├── CefVersion.props              # MSBuild import for CEF version properties (+ redist package version)
├── upgrade-cef.sh / .ps1         # Automated CEF upgrade steps (see UPGRADE.md)
│
├── CefGlue/                      # Core CefGlue library (.NET wrapper for CEF)
├── CefGlue.Interop.Gen/          # CEF C API headers + interop generator (cefglue_interop_gen.py)
├── CefGlue.Avalonia/             # Avalonia browser control implementation
├── CefGlue.WPF/                  # WPF browser control implementation
├── CefGlue.Common/               # Shared browser adapter code
├── CefGlue.Common.Shared/        # Shared utilities and serialization
├── CefGlue.BrowserProcess/       # Browser subprocess executable
├── CefGlue.BrowserProcess.Core/  # Render-process side (JS bridge, frame delivery)
├── CefGlue.Demo.Avalonia/        # Avalonia demo application
├── CefGlue.Demo.WPF/             # WPF demo application
├── CefGlue.Tests/                # Unit tests
├── Nuget/                        # NuGet packaging configuration
│
├── CefRuntime/                   # CEF redistribution NuGet packages (all RIDs, single project)
│   ├── CefRuntime.csproj         # SDK-style project; build with `dotnet pack --runtime <rid>`
│   ├── make_cefredist_linux.sh   # Downloads & stages CEF binaries for Linux
│   ├── make_cefredist_osx.sh     # Downloads & stages CEF binaries for macOS
│   ├── make_cefredist.ps1        # Windows wrapper that calls the appropriate sh script via WSL
│   ├── cef.runtime.<rid>.props   # MSBuild props injected into consuming projects
│   ├── deploy-cef-framework.sh   # macOS post-install helper
│   └── redist/                   # Staged CEF binaries (generated, git-ignored)
│
└── LocalPackages/                # Output folder for locally built NuGet packages
```

## Version Information

| Component | Version |
|-----------|---------|
| CEF | 154.0.33 |
| Chromium | 154.0.8037.94 |
| CefGlue | 154.8037.94 |
| Target Framework | .NET 10.0 |
| Avalonia | 11.3.14 |

### CEF 154: V8 extensions removed

CEF 154 removed V8 extensions: `CefRegisterExtension` is now a stub that does nothing and returns
`false`. CefGlue no longer uses it; the `cefglue` JavaScript bridge (object binding, promises and
script evaluation) is installed into each V8 context from `OnContextCreated` instead.
`CefRuntime.RegisterExtension` is marked `[Obsolete]`. If your own code calls it, inject your script
from `CefRenderProcessHandler.OnContextCreated` (e.g. with `CefV8Context.TryEval`) instead.

## Supported Platforms

| OS      | x64 | ARM64 | WPF | Avalonia | Avalonia XPF |
|---------|-----|-------|-----|----------|--------------|
| Windows | ✔️  | ✔️    | ✔️  | ✔️      | ✔️          |
| macOS   | ✔️  | ✔️    | ❌  | ✔️      | ✔️          |
| Linux   | ✔️  | 🔘    | ❌  | ✔️      | ✔️          |

✔️ Supported  
❌ Not supported  
🔘 Works with issues (see Linux ARM64 notes below)

## Getting Started

### Prerequisites

- .NET 10.0 SDK
- Visual Studio 2022 or VS Code with C# extension
- For Linux/macOS CEF package building:
  - `curl` or `aria2c`
  - `tar` with bzip2 support
  - `strip` (binutils)

### Building the Solution

1. Open `Xilium.CefGlue.slnx` (repository root) in Visual Studio or your preferred IDE, or build from the command line:
   ```bash
   dotnet restore Xilium.CefGlue.slnx
   dotnet build Xilium.CefGlue.slnx -c Release
   ```
2. Run the tests:
   ```bash
   dotnet test CefGlue.Tests/CefGlue.Tests.csproj -c Release
   ```

Restoring anything downstream of `CefGlue.Common` requires the `cef.runtime.*` packages and the official
`chromiumembeddedframework.runtime.*` packages for the current CEF version to exist (on nuget.org or in `LocalPackages/`).

### Running the Demo Applications

**WPF Demo (Windows only):**
```powershell
cd CefGlue.Demo.WPF
dotnet run -c Release
```

**Avalonia Demo (Cross-platform):**
```bash
cd CefGlue.Demo.Avalonia
dotnet run -c Release
```

## Building CEF Redistribution Packages

All four runtime packages are built from the single `CefRuntime/CefRuntime.csproj` project using `dotnet pack`. The `PrepareRedist` MSBuild target automatically invokes the appropriate download/staging script before the nuspec is assembled, so no manual script invocation is needed.

This will:
1. Download CEF binaries from Spotify's CDN (~375 MB per architecture)
2. Extracts the redistributable parts
3. Create NuGet packages in the `LocalPackages/` folder

```bash
cd CefRuntime
```

### macOS

```bash
# ARM64
dotnet pack CefRuntime.csproj --runtime osx-arm64

# x64
dotnet pack CefRuntime.csproj --runtime osx-x64
```

### Linux

```bash
# x64
dotnet pack CefRuntime.csproj --runtime linux-x64

# ARM64
dotnet pack CefRuntime.csproj --runtime linux-arm64
```

On **Windows** the `PrepareRedist` target calls `make_cefredist.ps1`, which delegates to the appropriate shell script via WSL. On **Linux/macOS** it calls the shell script directly.

Packed `.nupkg` files are written to `LocalPackages/`, which is already configured as a local NuGet feed for the solution.

## Linux ARM64 Notes

There are known issues with dynamic loading of CEF on ARM64 Linux due to TLS (Thread Local Storage) limitations. Workarounds include:

1. **Using `LD_PRELOAD`:**
   ```bash
   LD_PRELOAD=/path/to/libHarfBuzzSharp.so:/path/to/libcef.so ./YourApplication
   ```

2. **Patching ELF files:**
   ```bash
   patchelf --add-needed libHarfBuzzSharp.so --add-needed libcef.so path/to/YourApplication
   patchelf --add-needed libcef.so path/to/Xilium.CefGlue.BrowserProcess
   ```

See [LINUX.md](LINUX.md) for more details.

## Related Repositories

- [OutSystems/CefGlue](https://github.com/OutSystems/CefGlue) - Upstream CefGlue repository
- [OutSystems/cef.redist.linux](https://github.com/OutSystems/cef.redist.linux) - Linux CEF redistribution
- [OutSystems/cef.redist.osx](https://github.com/OutSystems/cef.redist.osx) - macOS CEF redistribution
- [Chromium Embedded Framework](https://bitbucket.org/chromiumembedded/cef) - CEF source
- [CEF Builds](https://cef-builds.spotifycdn.com/index.html) - Official CEF binary distributions

## License

- CefGlue is licensed under the MIT License
- CEF is licensed under the BSD License

## Acknowledgments

- Original CefGlue by [XiliumHQ](https://github.com/xiliumhq)
- Maintained by [OutSystems](https://github.com/OutSystems)
- The Chromium Embedded Framework Authors
