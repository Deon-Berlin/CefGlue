using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xilium.CefGlue.Interop;

namespace Xilium.CefGlue;

public static class CefRuntimeLocator
{
    private const string Icudtl= "icudtl.dat";
    
    private static readonly Dictionary<string, string> CachedPaths = new();
    
    public static string FindLibrary(string libraryName = libcef.DllName)
    {
        return FindFile(libraryName + CefRuntime.Platform switch
        {
            CefRuntimePlatform.MacOS => ".dylib",
            CefRuntimePlatform.Windows => ".dll",
            CefRuntimePlatform.Linux => ".so",
            _ => string.Empty
        }) ?? GetLibCefAlternative(libraryName);
    }

    public static string GetFrameworkDirPath() => FindLibrary() is {} f ? Path.GetDirectoryName(f) : null;
    public static string GetResourceDirPath() => FindFile(Icudtl) is {} f ? Path.GetDirectoryName(f) : null;
    public static string GetMainBundlePath() => Path.GetDirectoryName(GetRootPath());

    private static string FindFile(string fileName)
    {
        if (CachedPaths.TryGetValue(fileName, out var path)) return path;
        
        // search in common resolve paths
        foreach (var libPath in GetCommonResolvePaths().Select(d => Path.Combine(d, fileName)))
        {
            if (File.Exists(libPath)) return CachedPaths[fileName] = libPath;
        }
        
        var rootPath = GetRootPath();
        
        // Search in AppDomain base directory and subdirectories
        try
        {
            var found = Directory.EnumerateFiles(rootPath, fileName, SearchOption.AllDirectories).FirstOrDefault();
            if (found != null) return CachedPaths[fileName] = found;
        }
        catch { /* Ignore search errors */ }

        // Search upward from base directory (for subprocess scenarios)
        var searchDir = rootPath;
        for (var i = 0; i < 5; i++) // Search up to 5 levels up
        {
            try
            {
                var found = Directory.EnumerateFiles(searchDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) return CachedPaths[fileName] = found;
            }
            catch { /* Ignore search errors */ }

            // Also check for CEF subdirectory specifically
            var cefDir = Path.Combine(searchDir, "CEF");
            if (Directory.Exists(cefDir))
            {
                var cefLibPath = Path.Combine(cefDir, fileName);
                if (File.Exists(cefLibPath)) return CachedPaths[fileName] = cefLibPath;
            }

            var parent = Directory.GetParent(searchDir);
            if (parent == null || parent.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) break;
            searchDir = parent.FullName;
        }

        return null;
    }
    
    private static IEnumerable<string> GetCommonResolvePaths()
    {
        yield return AppDomain.CurrentDomain.BaseDirectory;
        yield return Environment.CurrentDirectory;
        
        if (CefRuntime.Platform != CefRuntimePlatform.MacOS) yield break;

        if (!AppDomain.CurrentDomain.BaseDirectory.EndsWith("MonoBundle")) yield break;
        
        var contentDir = GetRootPath();
        yield return Path.Combine(contentDir, "Resources");
        yield return Path.Combine(contentDir, "Frameworks", "Chromium Embedded Framework.framework");
        yield return Path.Combine(contentDir, "Frameworks", "Chromium Embedded Framework.framework", "Libraries");
        yield return Path.Combine(contentDir, "Frameworks", "Chromium Embedded Framework.framework", "Resources");

        // Nested CEF helper bundle (<App>.app/Contents/Frameworks/<Helper>.app): the framework lives in
        // the ENCLOSING app's Frameworks dir, not the helper's own. Resolve it there so helpers can share
        // the single framework without an out-of-bundle symlink (which codesign/notarization reject).
        var enclosingFrameworks = Directory.GetParent(contentDir)?.Parent?.FullName;
        if (enclosingFrameworks != null && Path.GetFileName(enclosingFrameworks) == "Frameworks")
        {
            var fw = Path.Combine(enclosingFrameworks, "Chromium Embedded Framework.framework");
            yield return fw;
            yield return Path.Combine(fw, "Libraries");
            yield return Path.Combine(fw, "Resources");
        }
    }
    
    private static string GetRootPath()
    {
        if (CefRuntime.Platform != CefRuntimePlatform.MacOS || AppDomain.CurrentDomain.BaseDirectory.EndsWith("Contents")) return AppDomain.CurrentDomain.BaseDirectory;

        return AppDomain.CurrentDomain.BaseDirectory.Contains("Contents") 
            ? Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory) ?? AppDomain.CurrentDomain.BaseDirectory 
            : AppDomain.CurrentDomain.BaseDirectory;
    }

    private static string GetLibCefAlternative(string libName)
    {
        if (libName != libcef.DllName || CefRuntime.Platform != CefRuntimePlatform.MacOS) return null;
        if (FindFile("Chromium Embedded Framework") is not { } frameworkPath) return null;
        CachedPaths[libName] = frameworkPath;
        return frameworkPath;
    }
}
