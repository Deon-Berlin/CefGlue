namespace Xilium.CefGlue.Common.Platform
{
    public delegate void KeyEventHandler(CefKeyEvent e, out bool handled);
    public delegate void TextInputEventHandler(string text, out bool handled);

    public interface IOffScreenKeyboardHandler
    {
        event KeyEventHandler KeyDown;
        event KeyEventHandler KeyUp;
        event TextInputEventHandler TextInput;
    }
}
