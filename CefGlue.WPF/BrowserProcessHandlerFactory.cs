using Xilium.CefGlue.Common.Handlers;

namespace Xilium.CefGlue.WPF;

public static class BrowserProcessHandlerFactory
{
    private static readonly BrowserProcessHandler Default = new CompositionBrowserProcessHandler();
    public static BrowserProcessHandler Custom { get; set; }
    public static BrowserProcessHandler Current => Custom ?? Default;
}
