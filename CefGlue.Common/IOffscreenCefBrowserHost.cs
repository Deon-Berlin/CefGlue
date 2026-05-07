using System;

namespace Xilium.CefGlue.Common
{
    internal interface IOffscreenCefBrowserHost : ICefBrowserHost
    {
        public event EventHandler<PaintEventArgs> Paint;

        void GetViewRect(out CefRectangle rect);
        void GetScreenPoint(int viewX, int viewY, ref int screenX, ref int screenY);
        void GetScreenInfo(CefScreenInfo screenInfo);

        void HandlePopupShow(bool show);
        void HandlePopupSizeChange(CefRectangle rect);

        void HandleViewPaint(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects, bool isPopup);

        void HandleStartDragging(CefBrowser browser, CefDragData dragData, CefDragOperationsMask allowedOps, int x, int y);
        void HandleUpdateDragCursor(CefBrowser browser, CefDragOperationsMask operation);
    }

    public class PaintEventArgs(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects, bool isPopup)
        : EventArgs
    {
        public IntPtr Buffer { get; } = buffer;
        public int Width { get; } = width;
        public int Height { get; } = height;
        public CefRectangle[] DirtyRects { get; } = dirtyRects;
        public bool IsPopup { get; } = isPopup;
    }
}
