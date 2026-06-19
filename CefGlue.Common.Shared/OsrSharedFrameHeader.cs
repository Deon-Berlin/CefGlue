using System;
using System.Buffers.Binary;

namespace Xilium.CefGlue.Common.Shared
{
    /// <summary>
    /// Wire-format header at offset 0 of an OSR shared-memory frame region (pixels follow at
    /// <see cref="HeaderSize"/>). Written by the browser process (app transport) and read by the
    /// host render process (see SharedFrameDeliveryRenderSide). Little-endian; both sides are the
    /// same machine. <c>Kind</c> reserves 0 = CPU RGBA, 1 = (future) accelerated/IOSurface handle.
    /// </summary>
    public static class OsrSharedFrameHeader
    {
        public const int HeaderSize = 32; // 5 ints used, padded for alignment / future fields
        private const int OffKind = 0, OffBrowserId = 4, OffWidth = 8, OffHeight = 12, OffStride = 16;

        public readonly struct Fields
        {
            public Fields(int kind, int browserId, int width, int height, int stride)
            { Kind = kind; BrowserId = browserId; Width = width; Height = height; Stride = stride; }
            public int Kind { get; }
            public int BrowserId { get; }
            public int Width { get; }
            public int Height { get; }
            public int Stride { get; }
        }

        public static void Write(Span<byte> dst, int kind, int browserId, int width, int height, int stride)
        {
            BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(OffKind), kind);
            BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(OffBrowserId), browserId);
            BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(OffWidth), width);
            BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(OffHeight), height);
            BinaryPrimitives.WriteInt32LittleEndian(dst.Slice(OffStride), stride);
        }

        public static Fields Read(ReadOnlySpan<byte> src) => new Fields(
            BinaryPrimitives.ReadInt32LittleEndian(src.Slice(OffKind)),
            BinaryPrimitives.ReadInt32LittleEndian(src.Slice(OffBrowserId)),
            BinaryPrimitives.ReadInt32LittleEndian(src.Slice(OffWidth)),
            BinaryPrimitives.ReadInt32LittleEndian(src.Slice(OffHeight)),
            BinaryPrimitives.ReadInt32LittleEndian(src.Slice(OffStride)));
    }
}
