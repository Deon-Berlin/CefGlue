using System;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Xilium.CefGlue;

namespace CefGlue
{
    internal static class NativeLibsLoader
    {
        /// <summary>
        /// Installs a native lib loader for loading cef native libs from their right location.
        /// Supports custom browser process scenarios where dylibs may be in Contents/MonoBundle or Contents/Frameworks.
        /// </summary>
        public static void Install()
        {
#if NET5_0_OR_GREATER
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, libName) =>
            {
                if (CefRuntimeLocator.FindLibrary(libName) is { } libPath)
                {
                    return NativeLibrary.Load(libPath);
                }
                
                // Return IntPtr.Zero to let the default resolution continue
                return IntPtr.Zero;
            };
#endif
        }
    }
}
