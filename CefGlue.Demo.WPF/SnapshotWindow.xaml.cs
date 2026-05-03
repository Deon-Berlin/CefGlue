using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Events;

namespace Xilium.CefGlue.Demo.WPF
{
    public partial class SnapshotWindow : Window
    {
        private CefBrowser _browser;
        private bool _isLoading;
        private byte[] _lastFrame;
        private int _frameWidth;
        private int _frameHeight;

        public SnapshotWindow()
        {
            InitializeComponent();
        }

        private void OnCaptureClick(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
                return;

            var url = addressTextBox.Text;
            if (string.IsNullOrWhiteSpace(url))
            {
                statusText.Text = "Please enter a URL";
                return;
            }

            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                url = "https://" + url;
                addressTextBox.Text = url;
            }

            StartCapture(url);
        }

        private void StartCapture(string url)
        {
            _isLoading = true;
            _lastFrame = null;
            captureButton.IsEnabled = false;
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;
            statusText.Text = "Loading...";

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
                System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ =>
                {
                    Dispatcher.BeginInvoke((Action)SaveSnapshot);
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
                    statusText.Text = "No frame captured";
                    return;
                }

                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var fileName = $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var filePath = Path.Combine(desktopPath, fileName);

                var bitmap = new WriteableBitmap(_frameWidth, _frameHeight, 96, 96, PixelFormats.Bgra32, null);
                bitmap.WritePixels(new Int32Rect(0, 0, _frameWidth, _frameHeight), _lastFrame, _frameWidth * 4, 0);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(stream);
                }

                statusText.Text = $"Snapshot saved to:\n{filePath}";
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _isLoading = false;
                captureButton.IsEnabled = true;
                progressBar.Visibility = Visibility.Collapsed;

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
                _owner.Dispatcher.BeginInvoke((Action)(() => _owner.OnBrowserCreated(browser)));
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
                _owner.Dispatcher.BeginInvoke((Action)(() => _owner.OnLoadingStateChange(isLoading)));
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

            protected override void OnAcceleratedPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, CefAcceleratedPaintInfo acceleratedPaintInfo) { }

            protected override void OnScrollOffsetChanged(CefBrowser browser, double x, double y) { }

            protected override void OnImeCompositionRangeChanged(CefBrowser browser, CefRange selectedRange, CefRectangle[] characterBounds) { }
        }
    }
}
