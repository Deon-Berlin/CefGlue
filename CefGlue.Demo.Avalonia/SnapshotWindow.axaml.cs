using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Xilium.CefGlue.Demo.Avalonia
{
    public partial class SnapshotWindow : Window
    {
        private CefBrowser _browser;
        private bool _isLoading;
        private byte[] _lastFrame;
        private int _frameWidth;
        private int _frameHeight;

        private TextBox _addressTextBox;
        private Button _captureButton;
        private ProgressBar _progressBar;
        private TextBlock _statusText;

        public SnapshotWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            _addressTextBox = this.FindControl<TextBox>("addressTextBox");
            _captureButton = this.FindControl<Button>("captureButton");
            _progressBar = this.FindControl<ProgressBar>("progressBar");
            _statusText = this.FindControl<TextBlock>("statusText");
        }

        private void OnCaptureClick(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
                return;

            var url = _addressTextBox.Text;
            if (string.IsNullOrWhiteSpace(url))
            {
                _statusText.Text = "Please enter a URL";
                return;
            }

            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                url = "https://" + url;
                _addressTextBox.Text = url;
            }

            StartCapture(url);
        }

        private void StartCapture(string url)
        {
            _isLoading = true;
            _lastFrame = null;
            _captureButton.IsEnabled = false;
            _progressBar.IsVisible = true;
            _statusText.Text = "Loading...";

            var windowInfo = CefWindowInfo.Create();
            windowInfo.SetAsWindowless(IntPtr.Zero, false);

            var browserSettings = new CefBrowserSettings
            {
                WindowlessFrameRate = 30
            };

            var client = new SnapshotCefClient(this);
            CefBrowserHost.CreateBrowser(windowInfo, client, browserSettings, url);
        }

        internal void OnBrowserCreated(CefBrowser browser)
        {
            _browser = browser;
        }

        internal void OnLoadingStateChange(bool isLoading)
        {
            if (!isLoading && _isLoading)
            {
                // Page finished loading, wait a bit for rendering to complete
                Task.Delay(1000).ContinueWith(_ =>
                {
                    Dispatcher.UIThread.Post(SaveSnapshot);
                });
            }
        }

        internal void OnPaint(IntPtr buffer, int width, int height)
        {
            _frameWidth = width;
            _frameHeight = height;
            _lastFrame = new byte[width * height * 4];
            System.Runtime.InteropServices.Marshal.Copy(buffer, _lastFrame, 0, _lastFrame.Length);
        }

        private void SaveSnapshot()
        {
            try
            {
                if (_lastFrame == null || _frameWidth == 0 || _frameHeight == 0)
                {
                    _statusText.Text = "No frame captured";
                    return;
                }

                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var fileName = $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var filePath = Path.Combine(desktopPath, fileName);

                // Convert BGRA to RGBA for Avalonia
                var rgbaData = new byte[_lastFrame.Length];
                for (int i = 0; i < _lastFrame.Length; i += 4)
                {
                    rgbaData[i] = _lastFrame[i + 2];     // R
                    rgbaData[i + 1] = _lastFrame[i + 1]; // G
                    rgbaData[i + 2] = _lastFrame[i];     // B
                    rgbaData[i + 3] = _lastFrame[i + 3]; // A
                }

                var bitmap = new WriteableBitmap(
                    new PixelSize(_frameWidth, _frameHeight),
                    new Vector(96, 96),
                    global::Avalonia.Platform.PixelFormat.Rgba8888,
                    AlphaFormat.Premul);

                using (var fb = bitmap.Lock())
                {
                    System.Runtime.InteropServices.Marshal.Copy(rgbaData, 0, fb.Address, rgbaData.Length);
                }

                bitmap.Save(filePath);

                _statusText.Text = $"Snapshot saved to:\n{filePath}";
            }
            catch (Exception ex)
            {
                _statusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _isLoading = false;
                _captureButton.IsEnabled = true;
                _progressBar.IsVisible = false;

                _browser?.GetHost()?.CloseBrowser(true);
                _browser = null;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _browser?.GetHost()?.CloseBrowser(true);
        }

        private class SnapshotCefClient : CefClient
        {
            private readonly SnapshotWindow _owner;
            private readonly SnapshotLoadHandler _loadHandler;
            private readonly SnapshotRenderHandler _renderHandler;
            private readonly SnapshotLifeSpanHandler _lifeSpanHandler;

            public SnapshotCefClient(SnapshotWindow owner)
            {
                _owner = owner;
                _loadHandler = new SnapshotLoadHandler(owner);
                _renderHandler = new SnapshotRenderHandler(owner);
                _lifeSpanHandler = new SnapshotLifeSpanHandler(owner);
            }

            protected override CefLoadHandler GetLoadHandler() => _loadHandler;
            protected override CefRenderHandler GetRenderHandler() => _renderHandler;
            protected override CefLifeSpanHandler GetLifeSpanHandler() => _lifeSpanHandler;
        }

        private class SnapshotLifeSpanHandler : CefLifeSpanHandler
        {
            private readonly SnapshotWindow _owner;

            public SnapshotLifeSpanHandler(SnapshotWindow owner)
            {
                _owner = owner;
            }

            protected override void OnAfterCreated(CefBrowser browser)
            {
                Dispatcher.UIThread.Post(() => _owner.OnBrowserCreated(browser));
            }
        }

        private class SnapshotLoadHandler : CefLoadHandler
        {
            private readonly SnapshotWindow _owner;

            public SnapshotLoadHandler(SnapshotWindow owner)
            {
                _owner = owner;
            }

            protected override void OnLoadingStateChange(CefBrowser browser, bool isLoading, bool canGoBack, bool canGoForward)
            {
                Dispatcher.UIThread.Post(() => _owner.OnLoadingStateChange(isLoading));
            }
        }

        private class SnapshotRenderHandler : CefRenderHandler
        {
            private readonly SnapshotWindow _owner;

            public SnapshotRenderHandler(SnapshotWindow owner)
            {
                _owner = owner;
            }

            protected override CefAccessibilityHandler GetAccessibilityHandler() => null;

            protected override bool GetRootScreenRect(CefBrowser browser, ref CefRectangle rect)
            {
                rect = new CefRectangle(0, 0, 1280, 720);
                return true;
            }

            protected override void GetViewRect(CefBrowser browser, out CefRectangle rect)
            {
                rect = new CefRectangle(0, 0, 1280, 720);
            }

            protected override bool GetScreenPoint(CefBrowser browser, int viewX, int viewY, ref int screenX, ref int screenY)
            {
                screenX = viewX;
                screenY = viewY;
                return true;
            }

            protected override bool GetScreenInfo(CefBrowser browser, CefScreenInfo screenInfo)
            {
                screenInfo.DeviceScaleFactor = 1.0f;
                return true;
            }

            protected override void OnPopupSize(CefBrowser browser, CefRectangle rect) { }

            protected override void OnPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, IntPtr buffer, int width, int height)
            {
                if (type == CefPaintElementType.View)
                {
                    _owner.OnPaint(buffer, width, height);
                }
            }

            protected override void OnAcceleratedPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, CefAcceleratedPaintInfo

acceleratedPaintInfo)
            { }

            protected override void OnScrollOffsetChanged(CefBrowser browser, double x, double y) { }

            protected override void OnImeCompositionRangeChanged(CefBrowser browser, CefRange selectedRange, CefRectangle[] characterBounds) { }
        }
    }
}
