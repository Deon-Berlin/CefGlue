using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CefGlue.Tests.CustomSchemes;
using CefGlue.Tests.Helpers;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Shared;

namespace CefGlue.Tests
{
    public class TestBase
    {
        private static object initLock = new object();
        private static bool initialized = false;

        private AvaloniaCefBrowser browser;
        private Window window;

        protected AvaloniaCefBrowser Browser => browser;

        // CEF requires every CachePath (global or per-request-context) to be equal to, or a
        // child of, CefSettings.RootCachePath. With the Chrome runtime an invalid/foreign
        // cache path aborts profile creation and crashes the browser process, so tests that
        // exercise a custom request-context cache path must place it under this root.
        protected static readonly string RootCachePath =
            Path.Combine(Path.GetTempPath(), "CefGlue.Tests.Cache");

        [OneTimeSetUp]
        protected async Task SetUp()
        {
            if (initialized)
            {
                return;
            }

            var initializationTaskCompletionSource = new TaskCompletionSource<bool>();

            // Under `dotnet test` the entry executable is the test host, which cannot be
            // used as the CEF sub-process (it is not modifiable to call CefSubProcess.Run).
            // Point CEF at the CefGlue.BrowserProcess executable instead. It is deployed
            // self-contained (with its own runtime) into a 'subprocess' subfolder by the
            // DeployCefSubprocess target; the exe copied to the output root by the plain
            // ProjectReference has no runtime beside it and would fail to launch. Fall back
            // to the output root if the subfolder deployment is not present.
            var subprocessName = "Xilium.CefGlue.BrowserProcess" + (OperatingSystem.IsWindows() ? ".exe" : "");
            var subprocessPath = Path.Combine(AppContext.BaseDirectory, "subprocess", subprocessName);
            if (!File.Exists(subprocessPath))
            {
                subprocessPath = Path.Combine(AppContext.BaseDirectory, subprocessName);
            }
            Directory.CreateDirectory(RootCachePath);
            var settings = new CefSettings()
            {
                BrowserSubprocessPath = subprocessPath,
                RootCachePath = RootCachePath
            };

            CefRuntimeLoader.Initialize(settings: settings, customSchemes: new[] {
                new CustomScheme()
                {
                    SchemeName = CustomSchemeHandlerFactory.SchemeName,
                    SchemeHandlerFactory = new CustomSchemeHandlerFactory()
                }
            });

            lock (initLock)
            {
                if (initialized)
                {
                    return;
                }

                var uiThread = new Thread(() =>
                {
                    AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();

                    Dispatcher.UIThread.Post(() =>
                    {
                        initialized = true;
                        initializationTaskCompletionSource.SetResult(true);
                    });
                    Dispatcher.UIThread.MainLoop(CancellationToken.None);
                });
                uiThread.IsBackground = true;
                uiThread.Start();
            }

            await initializationTaskCompletionSource.Task;
        }

        [SetUp]
        protected virtual async Task Setup()
        {
            await InternalSetup(() => new AvaloniaCefBrowser());

            await ExtraSetup();
        }

        protected async Task InternalSetup(Func<AvaloniaCefBrowser> avaloniaCefBrowserFactory)
        {
            var testName = TestContext.CurrentContext.Test.FullName; // capture test name outside the async part (otherwise wont work properly)
            await Run(async () =>
            {
                if (window == null)
                {
                    window = new Window();
                    window.Width = 1;
                    window.Height = 1;

                    window.Show();
                }

                window.Title = testName;

                var browserInitTaskCompletionSource = new TaskCompletionSource<bool>();
                browser = avaloniaCefBrowserFactory();
                browser.BrowserInitialized += delegate () { browserInitTaskCompletionSource.SetResult(true); };

                window.Content = browser;

                await browserInitTaskCompletionSource.Task;
            });
        }

        protected virtual Task ExtraSetup()
        {
            return Task.CompletedTask;
        }

        [TearDown] 
        protected void TearDown()
        {
            browser?.Dispose();
        }

        [OneTimeTearDown]
        protected async Task OneTimeTearDown()
        {
            await Run(() => {
                window?.Close();
                window = null;
            });
        }

        protected Task Run(Func<Task> func) => Dispatcher.UIThread.InvokeAsync(func, DispatcherPriority.Background);

        protected Task Run(Action action) => Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Background).GetTask();

        protected Task<T> EvaluateJavascript<T>(string script, TimeSpan? timeout = null) => Browser.EvaluateJavaScript<T>(script, timeout: timeout);
    }
}
