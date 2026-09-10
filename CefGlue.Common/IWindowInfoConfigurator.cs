namespace Xilium.CefGlue.Common
{
    /// <summary>
    /// Implemented by the browser controls to allow the host application to customize the
    /// native window information used by the browser adapters when creating browser windows.
    /// </summary>
    internal interface IWindowInfoConfigurator
    {
        /// <summary>
        /// Configures the window information used to create the browser window.
        /// Called after the default window configuration was applied and before the browser is created.
        /// </summary>
        void ConfigureWindowInfo(CefWindowInfo windowInfo);

        /// <summary>
        /// Configures the window information used to create the developer tools window.
        /// Only called when the developer tools window is created with the default window information.
        /// </summary>
        void ConfigureDevToolsWindowInfo(CefWindowInfo windowInfo);
    }
}
