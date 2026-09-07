using System;
using Xilium.CefGlue.Common.Shared;
using Xilium.CefGlue.Common.Shared.Helpers;
using Xilium.CefGlue.Common.Shared.RendererProcessCommunication;

namespace Xilium.CefGlue.BrowserProcess.FrameDelivery
{
    /// <summary>
    /// Render-side receiver for <see cref="Messages.OsrFrame"/>. Opens the named shared-memory
    /// region, reads the active double-buffer slot, copies it into a JS ArrayBuffer, and calls
    /// the page's <c>window.__cefOnFrame(browserId, width, height, buffer)</c> if present.
    ///
    /// <para>The region is opened once and <b>kept mapped</b> across frames, keyed by browser.
    /// Re-opening it per frame would re-establish every page-table entry on each paint and give
    /// back much of what a persistent region on the writer side exists to save. The mapping is
    /// replaced only when the name changes, which is what an OSR resize does — it recreates the
    /// region under a bumped generation.</para>
    /// </summary>
    internal sealed unsafe class FrameDeliveryRenderSide
    {
        private const string JsCallbackName = "__cefOnFrame";

        private readonly OsrRegionCache _regions = new OsrRegionCache();

        public FrameDeliveryRenderSide(MessageDispatcher dispatcher)
        {
            dispatcher.RegisterMessageHandler(Messages.OsrFrame.Name, Handle);
        }

        private void Handle(MessageReceivedEventArgs args)
        {
            var msg = Messages.OsrFrame.FromCefMessage(args.Message);

            // How much of the region this frame needs. Computed before the region is opened,
            // because the opener has to be told: macOS will not report a shared-memory object's
            // size, so an undersized region is caught by asking for the right length rather than
            // by measuring afterwards.
            long pixelBytes = (long)msg.Stride * msg.Height;
            long required = msg.HeaderSize + 2L * pixelBytes; // header + two buffers
            if (pixelBytes <= 0 || required <= 0) return;

            var region = _regions.Resolve(msg.BrowserId, msg.MapName, required);
            if (region == null) return;

            // A mapping cached from an earlier frame can be shorter than this one needs, so the
            // size is still checked before any unsafe read.
            if (required > region.Length) return;

            byte* basePtr = region.Pointer;
            int active = System.Threading.Volatile.Read(ref *(int*)(basePtr + msg.ActiveOffset));
            if ((uint)active > 1u) return; // corrupt/stale header: index must be 0 or 1
            var bufferPtr = (IntPtr)(basePtr + msg.HeaderSize + active * pixelBytes);

            var frame = args.Browser.GetMainFrame();
            var context = frame?.V8Context;
            if (context == null || !context.Enter()) return;
            try
            {
                var global = context.GetGlobal();
                if (!global.HasValue(JsCallbackName)) return;
                var callback = global.GetValue(JsCallbackName);
                if (!callback.IsFunction) return;

                var arrayBuffer = CefV8Value.CreateArrayBufferWithCopy(bufferPtr, (ulong)pixelBytes);
                var jsArgs = new[]
                {
                    CefV8Value.CreateInt(msg.BrowserId),
                    CefV8Value.CreateInt(msg.Width),
                    CefV8Value.CreateInt(msg.Height),
                    arrayBuffer
                };
                callback.ExecuteFunction(null, jsArgs);
            }
            finally
            {
                context.Exit();
            }
        }
    }
}
