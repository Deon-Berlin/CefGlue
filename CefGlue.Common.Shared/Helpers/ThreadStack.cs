using System.Runtime.InteropServices;

namespace Xilium.CefGlue.Common.Shared.Helpers
{
    public static class ThreadStack
    {
        [DllImport("kernel32.dll")]
        private static extern void GetCurrentThreadStackLimits(out nuint lowLimit, out nuint highLimit);

        public static nuint GetSize()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return 0;

            GetCurrentThreadStackLimits(out var low, out var high);
            return (high - low) / 1024;
        }
    }
}
