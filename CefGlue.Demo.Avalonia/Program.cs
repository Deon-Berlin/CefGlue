using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xilium.CefGlue.BrowserProcess;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Shared;

namespace Xilium.CefGlue.Demo.Avalonia
{
    class Program
    {
        public static bool IsOSR { get; private set; }

        static int Main(string[] args)
        {
            StackDebug.Log(args, "Avalonia");
#if !MACOS
            CefSubProcess.Run(args, true);
#endif
            IsOSR = args.Any(x => x == "-osr");

            // generate a unique cache path to avoid problems when launching more than one process
            // https://www.magpcss.org/ceforum/viewtopic.php?f=6&t=19665
            var cachePath = Path.Combine(Path.GetTempPath(), "CefGlue", Environment.ProcessId.ToString());
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "logs");
            Directory.CreateDirectory(logPath);
            Console.WriteLine($"[Avalonia] CEF cache: {cachePath}");
            Console.WriteLine($"[Avalonia] CEF logs: {logPath}");

            AppDomain.CurrentDomain.ProcessExit += delegate { Cleanup(cachePath); };
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .With(new Win32PlatformOptions())
                .AfterSetup(_ => CefRuntimeLoader.Initialize(new CefSettings
                    {
                        RootCachePath = cachePath,
                        LogSeverity = CefLogSeverity.Verbose,
                        LogFile = Path.Combine(logPath, "cef_avalonia_debug.log"),
                        WindowlessRenderingEnabled = IsOSR,
#if !MACOS
                    //BrowserSubprocessPath = CefSubProcess.GetSubProcessPath(),
#endif
                },
                    customSchemes:
                    [
                        new CustomScheme { SchemeName = "test", SchemeHandlerFactory = new CustomSchemeHandler() }
                    ]))
                .StartWithClassicDesktopLifetime(args);
                      
            return 0;
        }

        private static void Cleanup(string cachePath)
        {
            CefRuntime.Shutdown(); // must shutdown cef to free cache files (so that cleanup is able to delete files)

            try
            {
                var dirInfo = new DirectoryInfo(cachePath);
                if (dirInfo.Exists)
                {
                    dirInfo.Delete(true);
                    return;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // ignore
            }
            catch (IOException)
            {
                // ignore
            }

        }
    }
}
