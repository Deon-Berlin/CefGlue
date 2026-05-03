//
// This file manually written from cef/include/internal/cef_types_component.h.
// C API name: cef_component_update_error_t.
//
namespace Xilium.CefGlue
{
    /// <summary>
    /// Component update error codes. Added in CEF 146.
    /// Maps to update_client::Error values from
    /// components/update_client/update_client_errors.h.
    /// </summary>
    public enum CefComponentUpdateError
    {
        /// No error.
        None = 0,
        /// An update is already in progress for this component.
        UpdateInProgress = 1,
        /// The update was canceled.
        UpdateCanceled = 2,
        /// The update should be retried later.
        RetryLater = 3,
        /// A service error occurred.
        ServiceError = 4,
        /// An error occurred during the update check.
        UpdateCheckError = 5,
        /// The component was not found.
        CrxNotFound = 6,
        /// An invalid argument was provided.
        InvalidArgument = 7,
        /// Bad CRX data callback.
        BadCrxDataCallback = 8,
    }
}
