using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Open regions by browser id. Not synchronised: CEF delivers process messages on the
        /// render process's main thread, which is also the only thread that disposes them.
        /// </summary>
        private readonly Dictionary<int, SharedRegion> _regions = new Dictionary<int, SharedRegion>();

        public FrameDeliveryRenderSide(MessageDispatcher dispatcher)
        {
            dispatcher.RegisterMessageHandler(Messages.OsrFrame.Name, Handle);
        }

        private SharedRegion Resolve(int browserId, string mapName)
        {
            if (_regions.TryGetValue(browserId, out var cached))
            {
                if (cached.Name == mapName) return cached;
                cached.Dispose();
                _regions.Remove(browserId);
            }

            // The region may not exist yet, or the name may be stale after a resize bumped the
            // generation while a frame notify was in flight. Skipping the frame is correct — the
            // next paint carries the new name — and beats throwing at the frame rate.
            var region = SharedRegion.OpenExisting(mapName);
            if (region == null) return null;

            _regions[browserId] = region;
            return region;
        }

        private void Handle(MessageReceivedEventArgs args)
        {
            var msg = Messages.OsrFrame.FromCefMessage(args.Message);

            var region = Resolve(msg.BrowserId, msg.MapName);
            if (region == null) return;

            // Validate the message against the actual mapped size before any unsafe read, so a
            // stale/short map cannot drive an out-of-bounds access in the render process.
            long pixelBytes = (long)msg.Stride * msg.Height;
            long required = msg.HeaderSize + 2L * pixelBytes; // header + two buffers
            if (pixelBytes <= 0 || required > region.Length) return;

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
