using System;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.WPF.Platform;

namespace Xilium.CefGlue.WPF
{
    /// <summary>
    /// The WPF CEF browser.
    /// </summary>
    public class WpfCefBrowser : BaseCefBrowser
    {
        static WpfCefBrowser()
        {
            // this will support macOS when running with Avalonia XPF
            if (CefRuntime.Platform == CefRuntimePlatform.MacOS && !CefRuntimeLoader.IsLoaded)
            {
                CefRuntimeLoader.Load(BrowserProcessHandlerFactory.Current);
            }
        }
        
        public WpfCefBrowser() : this(null, null) { }

        public WpfCefBrowser(Func<CefRequestContext> cefRequestContextFactory, BrowserProcessHandler browserProcessHandler = null)
            : base(cefRequestContextFactory, browserProcessHandler)
        {
            KeyboardNavigation.SetAcceptsReturn(this, true);
        }

        internal override IControl CreateControl()
        {
            return new WpfControl(this);
        }

        internal override IOffScreenControlHost CreateOffScreenControlHost()
        {
            return new WpfOffScreenControlHost(this);
        }

        internal override IOffScreenPopupHost CreatePopupHost()
        {
            var popup = new Popup
            {
                PlacementTarget = this,
                Placement = PlacementMode.Relative,
            };

            return new WpfPopup(popup);
        }
    }
}
