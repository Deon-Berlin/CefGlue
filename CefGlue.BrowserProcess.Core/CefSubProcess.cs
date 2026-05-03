using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Xilium.CefGlue.BrowserProcess.Helpers;
using Xilium.CefGlue.Common.Shared;

namespace Xilium.CefGlue.BrowserProcess;

public static class CefSubProcess
{
    public static void Run(string[] args, bool exitAfterRun = true)
    {
        if (!args.Any(a => a.StartsWith("--type="))) return;
        RunCef(args);
        if (exitAfterRun) Environment.Exit(0);
    }

    public static string GetSubProcessPath() => CefRuntime.Platform switch
    {
       CefRuntimePlatform.MacOS => Environment.ProcessPath?.Replace("MonoBundle", "MacOS").Replace(".dll", ""),
       _ => Environment.ProcessPath
    };
    
    internal static void RunCef(string[] args)
    {
#if DEBUG
        try
        {
#endif
            var parentProcessId = GetArgumentValue(args, CommandLineArgs.ParentProcessId);
            if (parentProcessId != null && int.TryParse(parentProcessId, out var parentProcessIdAsInt))
            {
                ParentProcessMonitor.StartMonitoring(parentProcessIdAsInt);
            }

            CefRuntime.Load();

            var customSchemesArg = GetArgumentValue(args, CommandLineArgs.CustomScheme);
            var customSchemes = CustomScheme.FromCommandLineValue(customSchemesArg);

            // first argument is the path of the executable, but its ignored for now
            var mainArgs = new CefMainArgs(new[] { "BrowserProcess" }.Concat(args).ToArray());
            var exitCode = CefRuntime.ExecuteProcess(mainArgs, new RendererCefApp(customSchemes), IntPtr.Zero);

            // Log exit code for diagnostics - STATUS_STACK_BUFFER_OVERRUN = 0xC0000409 = 3221226505
            File.AppendAllText(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "logs", "stack.log"),
                $"ExecuteProcess returned: 0x{(uint)exitCode:X8} ({exitCode}) [pid={Environment.ProcessId}, type={GetArgumentValue(args, "type") ?? "browser"}]{Environment.NewLine}");


            if (exitCode != -1)
            {
                Environment.Exit(exitCode);
            }
#if DEBUG
        }
        catch (Exception e)
        {
            Debugger.Break();
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Debugger.Launch();
            }
            throw;
        }
#endif
    }
    
    private static string GetArgumentValue(string[] args, string argName)
    {
        var arg = args.FirstOrDefault(a => a?.StartsWith(argName + "=") == true);
        return arg?.Substring(argName.Length + 1) ?? "";
    }
}
