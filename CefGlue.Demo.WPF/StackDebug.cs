using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Xilium.CefGlue.Common.Shared.Helpers;

namespace Xilium.CefGlue.Demo
{
    internal static class StackDebug
    {
        [Conditional("DEBUG")]
        internal static void Log(string[] args, string prefix)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

            Directory.CreateDirectory(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "logs"));

            WriteAllocatedStackSize($"{prefix} Stack [{string.Join(",", args).Replace("--type=", "")}]");
        }
        
        private static void WriteAllocatedStackSize(string header)
        {
            
            // Log to file so renderer subprocess output is also visible
            var msg = $"{header,-25}: {ThreadStack.GetSize(),6} KB  [pid={Environment.ProcessId}]";
            Debug.WriteLine(msg);
            File.AppendAllText(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "logs", "stack.log"),
                msg + Environment.NewLine);
        }
    }
}
