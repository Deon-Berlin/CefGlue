using Avalonia.Controls;

namespace Xilium.CefGlue.Common
{
    partial class BaseCefBrowser : Control
    {
        public partial string Address { get => _adapter.Address; set => _adapter.Address = value; }
    }
}
