using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32.SafeHandles;

namespace Xilium.CefGlue.WPF.Platform;

/// <summary>
/// Enables to override platform specific functionality.
/// Helpful in Avalonia XPF context.
/// </summary>
public static class PlatformHelper
{
    public static Func<PresentationSource, float> GetDeviceScaleFactor { get; set; } =
        source => (float)(source?.CompositionTarget?.TransformToDevice.M11 ?? 1d);

    public static Func<IntPtr, CefCursorType, Cursor> GetCursor { get; set; } = (cursorHandle, cursorType) =>
    {
        // we do a platform check to enable usage in Avalonia XPF on non-Windows platforms 
        if (CefRuntime.Platform == CefRuntimePlatform.Windows)
        {
            return CursorInteropHelper.Create(new SafeFileHandle(cursorHandle, false));
        }

        return cursorType switch
        {
            CefCursorType.Hand => Cursors.Hand,
            CefCursorType.IBeam => Cursors.IBeam,
            CefCursorType.Cross => Cursors.Cross,
            CefCursorType.Wait => Cursors.Wait,
            CefCursorType.ColumnResize => Cursors.SizeWE,
            CefCursorType.RowResize => Cursors.SizeNS,
            CefCursorType.Help => Cursors.Help,
            _ => Cursors.Arrow
        };
    };
}
