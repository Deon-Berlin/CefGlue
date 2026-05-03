using System;
using System.Collections.Generic;
using System.IO;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Shared;

namespace Xilium.CefGlue.Common
{
    public static class CefRuntimeLoader
    {
        private static Action<BrowserProcessHandler> _delayedInitialization;

        public static void Initialize(CefSettings settings = null, KeyValuePair<string, string>[] flags = null, CustomScheme[] customSchemes = null)
        {
            _delayedInitialization = (browserProcessHandler) => InternalInitialize(settings, flags, customSchemes, browserProcessHandler);
        }

        private static void InternalInitialize(CefSettings settings = null, KeyValuePair<string, string>[] flags = null, CustomScheme[] customSchemes = null, BrowserProcessHandler browserProcessHandler = null)
        {
            CefRuntime.Load();

            if (settings == null)
            {
                settings = new CefSettings();
            }

            settings.UncaughtExceptionStackSize = 100; // for uncaught exception event work properly

            var basePath = AppContext.BaseDirectory;
            
            if (settings.BrowserSubprocessPath != null)
            {
                if (!File.Exists(settings.BrowserSubprocessPath))
                    throw new FileNotFoundException($"Specified BrowserSubprocessPath does not exist: {settings.BrowserSubprocessPath}");
            }

            switch (CefRuntime.Platform)
            {
                case CefRuntimePlatform.Windows:
                    settings.MultiThreadedMessageLoop = true;
                    break;

                case CefRuntimePlatform.MacOS:
                    
                    settings.NoSandbox = true;
                    settings.MultiThreadedMessageLoop = false;
                    settings.ExternalMessagePump = true;

                    // if a custom sub process is set, we need to configure the paths
                    if (settings.BrowserSubprocessPath != null)
                    {
                        if (CefRuntimeLocator.GetResourceDirPath() is not {} resourcesPath)
                        {
                            throw new FileNotFoundException($"Unable to find Resources folder.");
                        }
                        
                        settings.MainBundlePath = CefRuntimeLocator.GetMainBundlePath();
                        settings.FrameworkDirPath = CefRuntimeLocator.GetFrameworkDirPath();
                        settings.ResourcesDirPath = resourcesPath;    
                    }
                    else
                    {
                        // TODO: Check for helper apps in frameworks folder
                    }

                    break;
                
                case CefRuntimePlatform.Linux:
                    settings.NoSandbox = true;
                    settings.MultiThreadedMessageLoop = true;
                    break;
            }

            AppDomain.CurrentDomain.ProcessExit += delegate { CefRuntime.Shutdown(); };

            IsOSREnabled = settings.WindowlessRenderingEnabled;

            // On Linux, with osr disable, the filename in CefMainArgs will be used as accessible name.
            // If the name is empty, chromium will crash at ui::AXNodeData:SetNamechecked.
            var exeFileName = Path.GetFileName(Environment.ProcessPath);
            if (string.IsNullOrEmpty(exeFileName))
            {
                exeFileName = "CefGlue";
            }

            var args = new[] { 
                exeFileName,
#if DEBUG
                "--use-mock-keychain"
#endif
            };
            
            CefRuntime.Initialize(new CefMainArgs(args), settings, new BrowserCefApp(customSchemes, flags, browserProcessHandler), IntPtr.Zero);

            if (customSchemes != null)
            {
                foreach (var scheme in customSchemes)
                {
                    CefRuntime.RegisterSchemeHandlerFactory(scheme.SchemeName, scheme.DomainName, scheme.SchemeHandlerFactory);
                }
            }
        }

        internal static void Load(BrowserProcessHandler browserProcessHandler = null)
        {
            if (_delayedInitialization != null)
            {
                _delayedInitialization.Invoke(browserProcessHandler);
                _delayedInitialization = null;
            }
            else
            {
                InternalInitialize(browserProcessHandler: browserProcessHandler);
            }
        }

        public static bool IsLoaded => CefRuntime.IsInitialized;

        internal static bool IsOSREnabled { get; private set; }

        private static string BrowserProcessFileName
        {
            get
            {
                const string Filename = "Xilium.CefGlue.BrowserProcess";
                switch (CefRuntime.Platform)
                {
                    case CefRuntimePlatform.Windows:
                        return Filename + ".exe";
                    default:
                        return Filename;
                }
            }
        }
    }
}
