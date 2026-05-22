using System;
using System.Windows;
using System.Windows.Input;
using Xilium.CefGlue.Common.Platform;

namespace Xilium.CefGlue.WPF.Platform
{
    internal class WpfOffScreenKeyboardHandler : IOffScreenKeyboardHandler
    {
        public event Common.Platform.KeyEventHandler KeyDown;
        public event Common.Platform.KeyEventHandler KeyUp;
        public event TextInputEventHandler TextInput;

        public WpfOffScreenKeyboardHandler(FrameworkElement control)
        {
            ArgumentNullException.ThrowIfNull(control);

            control.KeyDown += OnKeyDown;
            control.KeyUp += OnKeyUp;
            control.TextInput += OnTextInput;
        }

        private void OnTextInput(object sender, TextCompositionEventArgs e)
        {
            var handled = false;
            TextInput?.Invoke(e.Text, out handled);
            e.Handled = handled;
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
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
