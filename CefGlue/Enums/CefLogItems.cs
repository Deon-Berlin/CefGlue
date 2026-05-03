//
// This file manually written from cef/include/internal/cef_types.h.
// C API name: cef_log_items_t.
//
namespace Xilium.CefGlue
{
    using System;

    /// <summary>
    /// Log items prepended to each log line.
    /// </summary>
    [Flags]
    public enum CefLogItems
    {
        /// Prepend the default list of items.
        Default = 0,
        /// Prepend no items.
        None = 1,
        /// Prepend the process ID.
        ProcessId = 1 << 1,
        /// Prepend the thread ID.
        ThreadId = 1 << 2,
        /// Prepend the timestamp.
        TimeStamp = 1 << 3,
        /// Prepend the tickcount.
        TickCount = 1 << 4,
    }
}
