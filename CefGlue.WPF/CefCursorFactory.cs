using System;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace Xilium.CefGlue.WPF;

public class CefCursorFactory : ICursorFactory
{
    public static ICursorFactory Default { get; } = new CefCursorFactory();
    public static ICursorFactory Custom { get; set; }
    public static ICursorFactory Current => Custom ?? Default;
    
    
    public Cursor Create(IntPtr cursorHandle, CefCursorType cursorType)
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
    }
}

public interface ICursorFactory
{
    Cursor Create(IntPtr cursorHandle, CefCursorType cursorType);
}
