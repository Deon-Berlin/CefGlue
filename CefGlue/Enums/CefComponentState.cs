//
// This file manually written from cef/include/internal/cef_types_component.h.
// C API name: cef_component_state_t.
//
namespace Xilium.CefGlue
{
    /// <summary>
    /// Component state values. Added in CEF 146.
    /// Maps to update_client::ComponentState values from
    /// components/update_client/update_client.h.
    /// A component is considered installed when its state is
    /// Updated, UpToDate, or Run.
    /// </summary>
    public enum CefComponentState
    {
        /// The component has not yet been checked for updates.
        New = 0,
        /// The component is being checked for updates now.
        Checking = 1,
        /// An update is available and will soon be processed.
        CanUpdate = 2,
        /// An update is being downloaded.
        Downloading = 3,
        /// An update is being decompressed.
        Decompressing = 4,
        /// A patch is being applied.
        Patching = 5,
        /// An update is being installed.
        Updating = 6,
        /// An update was successfully applied. The component is now installed.
        Updated = 7,
        /// The component was already up to date. The component is installed.
        UpToDate = 8,
        /// The service encountered an error during the update process.
        UpdateError = 9,
        /// The component is running a server-specified action. The component is installed.
        Run = 10,
    }
}
