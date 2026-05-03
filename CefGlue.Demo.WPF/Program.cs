using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Shared;

namespace Xilium.CefGlue.Demo.WPF
{
    internal static class Program
    {
        public static bool IsOSR { get; private set; }

        [STAThread]
        private static int Main(string[] args)
        {
            StackDebug.Log(args, "WPF");

            BrowserProcess.CefSubProcess.Run(args);

            IsOSR = args.Any(x => x == "-osr");

            // generate a unique cache path to avoid problems when launching more than one process
            // https://www.magpcss.org/ceforum/viewtopic.php?f=6&t=19665
            var cachePath = Path.Combine(Path.GetTempPath(), "CefGlue", Environment.ProcessId.ToString());
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "logs"); 
            Directory.CreateDirectory(logPath);
            
            AppDomain.CurrentDomain.ProcessExit += delegate { Cleanup(cachePath); };
            
            var settings = new CefSettings()
            {
                RootCachePath = cachePath,
                LogSeverity = CefLogSeverity.Verbose,
                LogFile = Path.Combine(logPath, "cef_debug.log"),
                WindowlessRenderingEnabled = IsOSR, // its recommended to leave this off (false), since its less performant and can cause more issues
            };
            CefRuntimeLoader.Initialize(settings, customSchemes:
            [
                new CustomScheme { SchemeName = "test", SchemeHandlerFactory = new CustomSchemeHandler() }
            ]);

            var app = new App();
            app.InitializeComponent();
            app.Run();

            return 0;
        }
        
        private static void Cleanup(string cachePath)
        {
            CefRuntime.Shutdown(); // must shutdown cef to free cache files (so that cleanup is able to delete files)

            try {
                var dirInfo = new DirectoryInfo(cachePath);
                if (dirInfo.Exists) {
                    dirInfo.Delete(true);
                }
            } catch (UnauthorizedAccessException) {
                // ignore
            } catch (IOException) {
                // ignore
            }
        }
    }

    class CustomSchemeHandler : CefSchemeHandlerFactory
    {
        protected override CefResourceHandler Create(CefBrowser browser, CefFrame frame, string schemeName, CefRequest request)
        {
            throw new System.NotImplementedException();
        }
    }
}
