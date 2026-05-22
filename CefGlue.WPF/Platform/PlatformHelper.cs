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
            CefCursorType.None => Cursors.None,
            CefCursorType.Hand => Cursors.Hand,
            CefCursorType.IBeam => Cursors.IBeam,
            CefCursorType.Progress => Cursors.AppStarting,
            CefCursorType.Wait => Cursors.Wait,
            CefCursorType.Help => Cursors.Help,
            CefCursorType.Cross => Cursors.Cross,
            CefCursorType.NoDrop => Cursors.No,
            CefCursorType.NotAllowed => Cursors.No,
            CefCursorType.ColumnResize => Cursors.SizeWE,
            CefCursorType.RowResize => Cursors.SizeNS,
            CefCursorType.EastWestResize => Cursors.SizeWE,
            CefCursorType.NorthSouthResize => Cursors.SizeNS,
            CefCursorType.NorthEastSouthWestResize => Cursors.SizeNESW,
            CefCursorType.NorthWestSouthEastResize => Cursors.SizeNWSE,
            // CefCursorType.Move => Cursors.SizeAll,
            // CefCursorType.MiddlePanning => Cursors.SizeAll,
            // CefCursorType.NorthEastPanning => Cursors.ScrollNE,
            // CefCursorType.NorthEastResize => Cursors.ScrollNE,
            // CefCursorType.NorthWestPanning => Cursors.ScrollNW,
            // CefCursorType.NorthWestResize => Cursors.ScrollNW,
            // CefCursorType.SouthEastPanning => Cursors.ScrollSE,
            // CefCursorType.SouthEastResize => Cursors.ScrollSE,
            // CefCursorType.SouthWestPanning => Cursors.ScrollSW,
            // CefCursorType.SouthWestResize => Cursors.ScrollSW,
            // CefCursorType.NorthResize => Cursors.ScrollN,
            // CefCursorType.NorthPanning => Cursors.ScrollN,
            // CefCursorType.EastResize => Cursors.ScrollE,
            // CefCursorType.EastPanning => Cursors.ScrollE,
            // CefCursorType.SouthResize => Cursors.ScrollS,
            // CefCursorType.SouthPanning => Cursors.ScrollS,
            // CefCursorType.WestResize => Cursors.ScrollW,
            // CefCursorType.WestPanning => Cursors.ScrollW,
            _ => Cursors.Arrow,
        };
    };
}
