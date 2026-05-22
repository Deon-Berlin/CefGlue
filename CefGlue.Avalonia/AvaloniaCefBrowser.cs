using System;
using Avalonia.Controls;
using Xilium.CefGlue.Avalonia.Platform;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Platform;

namespace Xilium.CefGlue.Avalonia
{
    /// <summary>
    /// The Avalonia CEF browser.
    /// </summary>
    public class AvaloniaCefBrowser(Func<CefRequestContext> cefRequestContextFactory = null)
        : BaseCefBrowser(cefRequestContextFactory)
    {
        static AvaloniaCefBrowser()
        {
            if (CefRuntime.Platform == CefRuntimePlatform.MacOS && !CefRuntimeLoader.IsLoaded)
            {
                CefRuntimeLoader.Load(new AvaloniaBrowserProcessHandler());
            }
        }

        internal override IControl CreateControl()
        {
            return new AvaloniaControl(this, VisualChildren);
        }

        internal override IOffScreenControlHost CreateOffScreenControlHost()
        {
            return new AvaloniaOffScreenControlHost(this, VisualChildren, CreateOffScreenKeyboardHandler(this));
        }

        public override IOffScreenKeyboardHandler CreateOffScreenKeyboardHandler(object control)
        {
            return new AvaloniaOffScreenKeyboardHandler(control as Control);
        }

        internal override IOffScreenPopupHost CreatePopupHost()
        {
            var popup = new ExtendedAvaloniaPopup
            {
                PlacementTarget = this
            };
            return new AvaloniaPopup(popup, popup.VisualChildren, CreateOffScreenKeyboardHandler(popup));
        }
    }
}
