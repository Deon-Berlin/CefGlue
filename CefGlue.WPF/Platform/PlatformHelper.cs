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
            _ => Cursors.Arrow
        };
    };

    public static Func<int, int, double, PixelFormat, WriteableBitmap> CreateBitmap { get; set; } =
        (width, height, dpi, pixelFormat) => new WriteableBitmap(width, height, dpi, dpi, pixelFormat, null);

    public static Func<KeyEventArgs, int> GetKeyCode { get; set; } = eventArgs =>
        KeyInterop.VirtualKeyFromKey(eventArgs.Key == Key.System ? eventArgs.SystemKey : eventArgs.Key);

    public static Func<KeyEventArgs, int> GetNativeKeyCode { get; set; } = eventArgs =>
        OperatingSystem.IsWindows()
            ? 0
            // MacOS uses a different keycode system
            // https://eastmanreference.com/complete-list-of-applescript-key-codes
            : (eventArgs.Key == Key.System ? eventArgs.SystemKey : eventArgs.Key) switch
            {
                Key.Left => 123,
                Key.Right => 124,
                Key.Up => 126,
                Key.Down => 125,
                Key.Enter => 36,
                Key.Back => 51,
                Key.Delete => 117,
                Key.LeftShift => 56,
                Key.RightShift => 60,
                Key.LeftCtrl => 59,
                Key.RightCtrl => 62,
                Key.LeftAlt => 58,
                Key.RightAlt => 61,
                Key.LWin => 55,
                Key.RWin => 54,
                Key.CapsLock => 57,
                Key.Tab => 48,
                Key.Space => 49,
                Key.Escape => 53,
                Key.Home => 115,
                Key.End => 119,
                Key.PageUp => 116,
                Key.PageDown => 121,
                Key.F1 => 122,
                Key.F2 => 120,
                Key.F3 => 99,
                Key.F4 => 118,
                Key.F5 => 96,
                Key.F6 => 97,
                Key.F7 => 98,
                Key.F8 => 100,
                Key.F9 => 101,
                Key.F10 => 109,
                Key.F11 => 103,
                Key.F12 => 111,
                Key.A => 0,
                Key.S => 1,
                Key.D => 2,
                Key.F => 3,
                Key.G => 5,
                Key.H => 4,
                Key.Z => 6,
                Key.X => 7,
                Key.C => 8,
                Key.V => 9,
                Key.B => 11,
                Key.Q => 12,
                Key.W => 13,
                Key.E => 14,
                Key.R => 15,
                Key.Y => 16,
                Key.T => 17,
                Key.N => 45,
                Key.M => 46,
                Key.O => 31,
                Key.U => 32,
                Key.I => 34,
                Key.P => 35,
                Key.L => 37,
                Key.J => 38,
                Key.K => 40,
                Key.D1 => 18,
                Key.D2 => 19,
                Key.D3 => 20,
                Key.D4 => 21,
                Key.D5 => 23,
                Key.D6 => 22,
                Key.D7 => 26,
                Key.D8 => 28,
                Key.D9 => 25,
                Key.D0 => 29,
                Key.OemComma => 43,
                Key.OemPeriod => 47,
                Key.OemMinus => 27,
                Key.OemSemicolon => 41,
                Key.OemQuotes => 39,
                Key.OemBackslash => 42,
                Key.OemOpenBrackets => 33,
                Key.OemCloseBrackets => 30,
                Key.OemPlus => 24,
                _ => 0
            };

    // Maps WPF Key to the macOS NSEvent.characters equivalent.
    // CEF's macOS platform delegate uses character/unmodified_character to determine the event type;
    // if both are 0, it overrides the event to NSFlagsChanged, causing keyup to appear as keydown in JS.
    public static Func<KeyEventArgs, char> GetMacOSCharacter { get; set; } = eventArgs =>
        eventArgs.Key switch
        {
            Key.Left     => '\uF702',  // NSLeftArrowFunctionKey
            Key.Right    => '\uF703',  // NSRightArrowFunctionKey
            Key.Down     => '\uF701',  // NSDownArrowFunctionKey
            Key.Up       => '\uF700',  // NSUpArrowFunctionKey
            Key.F1       => '\uF704',
            Key.F2       => '\uF705',
            Key.F3       => '\uF706',
            Key.F4       => '\uF707',
            Key.F5       => '\uF708',
            Key.F6       => '\uF709',
            Key.F7       => '\uF70A',
            Key.F8       => '\uF70B',
            Key.F9       => '\uF70C',
            Key.F10      => '\uF70D',
            Key.F11      => '\uF70E',
            Key.F12      => '\uF70F',
            Key.Delete   => '\uF728',  // NSDeleteFunctionKey (forward delete)
            Key.Home     => '\uF729',  // NSHomeFunctionKey
            Key.End      => '\uF72B',  // NSEndFunctionKey
            Key.PageUp   => '\uF72C',  // NSPageUpFunctionKey
            Key.PageDown => '\uF72D',  // NSPageDownFunctionKey
            Key.Insert   => '\uF727',  // NSInsertFunctionKey
            Key.Enter    => '\r',
            Key.Back     => '\u007F',  // Backspace (DEL on macOS)
            Key.Tab      => '\t',
            Key.Escape   => '\u001B',
            Key.Space    => ' ',
            Key.A => 'a', Key.B => 'b', Key.C => 'c', Key.D => 'd',
            Key.E => 'e', Key.F => 'f', Key.G => 'g', Key.H => 'h',
            Key.I => 'i', Key.J => 'j', Key.K => 'k', Key.L => 'l',
            Key.M => 'm', Key.N => 'n', Key.O => 'o', Key.P => 'p',
            Key.Q => 'q', Key.R => 'r', Key.S => 's', Key.T => 't',
            Key.U => 'u', Key.V => 'v', Key.W => 'w', Key.X => 'x',
            Key.Y => 'y', Key.Z => 'z',
            Key.D0 => '0', Key.D1 => '1', Key.D2 => '2', Key.D3 => '3',
            Key.D4 => '4', Key.D5 => '5', Key.D6 => '6', Key.D7 => '7',
            Key.D8 => '8', Key.D9 => '9',
            Key.OemComma => ',', Key.OemPeriod => '.', Key.OemMinus => '-',
            Key.OemSemicolon => ';', Key.OemQuotes => '\'', Key.OemBackslash => '\\',
            Key.OemOpenBrackets => '[', Key.OemCloseBrackets => ']', Key.OemPlus => '=',
            _ => '\0'
        };

}