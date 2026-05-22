using System;
using Avalonia.Controls;
using Avalonia.Input;
using Xilium.CefGlue.Common.Platform;

namespace Xilium.CefGlue.Avalonia.Platform
{
    internal class AvaloniaOffScreenKeyboardHandler : IOffScreenKeyboardHandler
    {
        private static readonly KeyModifiers ClipboardModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        
        public event KeyEventHandler KeyDown;
        public event KeyEventHandler KeyUp;
        public event TextInputEventHandler TextInput;
        public event CopyToClipboardEventHandler CopyToClipboard;
        public event PasteFromClipboardEventHandler PasteFromClipboard;

        public AvaloniaOffScreenKeyboardHandler(Control control)
        {
            ArgumentNullException.ThrowIfNull(control);

            control.KeyDown += OnKeyDown;
            control.KeyUp += OnKeyUp;
            control.TextInput += OnTextInput;
        }

        private void OnTextInput(object sender, TextInputEventArgs e)
        {
            var handled = false;
            TextInput?.Invoke(e.Text, out handled);
            e.Handled = handled;
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyModifiers.HasFlag(ClipboardModifier))
            {
                switch (e.Key)
                {
                    case Key.C:
                        CopyToClipboard?.Invoke(false);
                        break;
                    case Key.X:
                        CopyToClipboard?.Invoke(true);
                        break;
                    case Key.V:
                        PasteFromClipboard?.Invoke();
                        break;
                }
                
                e.Handled = true;
                return;
            }
            
            var handled = false;
            KeyUp?.Invoke(e.AsCefKeyEvent(true), out handled);
            e.Handled = handled;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            var handled = false;
            KeyDown?.Invoke(e.AsCefKeyEvent(false), out handled);

            var key = e.Key;
            if (key == Key.Tab  // Avoid tabbing out the web browser control
                || key == Key.Home || key == Key.End // Prevent keyboard navigation using home and end keys
                || key == Key.Up || key == Key.Down || key == Key.Left || key == Key.Right // Prevent keyboard navigation using arrows
               )
            {
                handled = true;
            }

            e.Handled = handled;
        }
    }
}
