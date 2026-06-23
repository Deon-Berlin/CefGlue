using System;
using System.IO.MemoryMappedFiles;
using Xilium.CefGlue.Common.Shared.Helpers;
using Xilium.CefGlue.Common.Shared.RendererProcessCommunication;

namespace Xilium.CefGlue.BrowserProcess.FrameDelivery
{
    /// <summary>
    /// Render-side receiver for <see cref="Messages.OsrFrame"/>. Opens the named shared-memory
    /// region, reads the active double-buffer slot, copies it into a JS ArrayBuffer, and calls
    /// the page's <c>window.__cefOnFrame(browserId, width, height, buffer)</c> if present.
    /// </summary>
    internal sealed unsafe class FrameDeliveryRenderSide
    {
        private const string JsCallbackName = "__cefOnFrame";

        public FrameDeliveryRenderSide(MessageDispatcher dispatcher)
        {
            dispatcher.RegisterMessageHandler(Messages.OsrFrame.Name, Handle);
        }

        private void Handle(MessageReceivedEventArgs args)
        {
            var msg = Messages.OsrFrame.FromCefMessage(args.Message);

            // The map may not exist yet, or the name may be stale after an OSR resize bumped the
            // generation while a frame notify was in flight. Skip the frame instead of throwing
            // (which would otherwise spam UnhandledException reports at the frame rate).
            MemoryMappedFile mmf;
            try { mmf = MemoryMappedFile.OpenExisting(msg.MapName); }
            catch { return; }

            using (mmf)
            using (var view = mmf.CreateViewAccessor())
            {
                byte* basePtr = null;
                view.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);
                try
                {
                    // Validate the message against the actual mapped size before any unsafe read,
                    // so a stale/short map cannot drive an out-of-bounds access in the render process.
                    long pixelBytes = (long)msg.Stride * msg.Height;
                    long required = msg.HeaderSize + 2L * pixelBytes; // header + two buffers
                    if (pixelBytes <= 0 || required > (long)view.SafeMemoryMappedViewHandle.ByteLength) return;

                    int active = System.Threading.Volatile.Read(ref *(int*)(basePtr + msg.ActiveOffset));
                    if ((uint)active > 1u) return; // corrupt/stale header: index must be 0 or 1
                    IntPtr bufferPtr = (IntPtr)(basePtr + msg.HeaderSize + active * pixelBytes);

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
                finally
                {
                    view.SafeMemoryMappedViewHandle.ReleasePointer();
                }
            }
        }
    }
}
