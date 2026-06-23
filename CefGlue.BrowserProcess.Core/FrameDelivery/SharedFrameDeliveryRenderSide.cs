using System;
using Xilium.CefGlue.Common.Shared;
using Xilium.CefGlue.Common.Shared.Helpers;

namespace Xilium.CefGlue.BrowserProcess.FrameDelivery
{
    /// <summary>
    /// Render-side receiver for the "cefOsrFrame" shared-memory frame message. Reads the region,
    /// parses <see cref="OsrSharedFrameHeader"/>, copies the RGBA pixels into a JS ArrayBuffer, and
    /// calls <c>window.__cefOnFrame(browserId, width, height, buffer)</c> if present. Cross-platform
    /// (no named MMF): the region travels with the message and CEF manages its lifetime.
    /// </summary>
    internal sealed unsafe class SharedFrameDeliveryRenderSide
    {
        public const string MessageName = "cefOsrFrame";
        private const string JsCallbackName = "__cefOnFrame";

        public SharedFrameDeliveryRenderSide(MessageDispatcher dispatcher)
        {
            dispatcher.RegisterMessageHandler(MessageName, Handle);
        }

        private void Handle(MessageReceivedEventArgs args)
        {
            using var region = args.Message.GetSharedMemoryRegion();
            if (region == null || !region.IsValid) return;

            long size = (long)region.Size;
            if (size <= OsrSharedFrameHeader.HeaderSize) return;

            byte* basePtr = (byte*)region.Memory();
            if (basePtr == null) return;
            var headerSpan = new ReadOnlySpan<byte>(basePtr, OsrSharedFrameHeader.HeaderSize);
            var header = OsrSharedFrameHeader.Read(headerSpan);

            // Only the CPU RGBA frame kind is handled here; a future accelerated kind (e.g. an
            // IOSurface/shared-texture handle) is delivered through a different path, so ignore it.
            if (header.Kind != 0) return;

            long pixelBytes = (long)header.Stride * header.Height;
            if (header.Width <= 0 || header.Height <= 0 || pixelBytes <= 0) return;
            if (header.Stride < header.Width * 4) return; // tight RGBA rows expected; reject malformed stride
            if (OsrSharedFrameHeader.HeaderSize + pixelBytes > size) return;

            IntPtr pixelPtr = (IntPtr)(basePtr + OsrSharedFrameHeader.HeaderSize);

            var frame = args.Browser.GetMainFrame();
            var context = frame?.V8Context;
            if (context == null || !context.Enter()) return;
            try
            {
                var global = context.GetGlobal();
                if (!global.HasValue(JsCallbackName)) return;
                var callback = global.GetValue(JsCallbackName);
                if (!callback.IsFunction) return;

                var arrayBuffer = CefV8Value.CreateArrayBufferWithCopy(pixelPtr, (ulong)pixelBytes);
                var jsArgs = new[]
                {
                    CefV8Value.CreateInt(header.BrowserId),
                    CefV8Value.CreateInt(header.Width),
                    CefV8Value.CreateInt(header.Height),
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
