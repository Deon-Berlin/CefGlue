using Xilium.CefGlue.Common.Shared.RendererProcessCommunication;

namespace Xilium.CefGlue.Common.Shared
{
    /// <summary>
    /// Browser-process helper to notify a host browser's render process that an offscreen
    /// browser has a new frame available in a named shared-memory region. The render side opens
    /// the region by name and delivers the pixels to JS (see FrameDeliveryRenderSide).
    /// </summary>
    public static class OsrFrameChannel
    {
        public static void Send(CefBrowser hostBrowser, int browserId, string mapName,
            int width, int height, int stride, int headerSize, int activeOffset)
        {
            var msg = new Messages.OsrFrame
            {
                BrowserId = browserId,
                MapName = mapName,
                Width = width,
                Height = height,
                Stride = stride,
                HeaderSize = headerSize,
                ActiveOffset = activeOffset
            };
            using var cefMessage = msg.ToCefProcessMessage();
            using var frame = hostBrowser.GetMainFrame();
            frame?.SendProcessMessage(CefProcessId.Renderer, cefMessage);
        }

        /// <summary>
        /// Tell the host's render process that an offscreen browser is gone, so it can release the
        /// region it has been keeping mapped for it. The counterpart to <see cref="Send"/>; both
        /// leave on the CEF UI thread in program order, so this cannot overtake a frame notify.
        /// </summary>
        public static void SendGone(CefBrowser hostBrowser, int browserId)
        {
            var msg = new Messages.OsrBrowserGone
            {
                BrowserId = browserId
            };

            using var cefMessage = msg.ToCefProcessMessage();
            using var frame = hostBrowser.GetMainFrame();
            frame?.SendProcessMessage(CefProcessId.Renderer, cefMessage);
        }
    }
}
