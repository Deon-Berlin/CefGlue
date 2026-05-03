//
// This file manually written from cef/include/internal/cef_types_component.h.
// C API name: cef_component_update_priority_t.
//
namespace Xilium.CefGlue
{
    /// <summary>
    /// Component update priority. Added in CEF 146.
    /// Maps to component_updater::OnDemandUpdater::Priority.
    /// </summary>
    public enum CefComponentUpdatePriority
    {
        /// Background priority. Update requests may be queued.
        Background = 0,
        /// Foreground priority. Update requests are processed immediately.
        Foreground = 1,
    }
}
