using Xilium.CefGlue.Demo;

namespace Xilium.CefGlue.BrowserProcess
{
    class Program
    {
        static void Main(string[] args)
        {
            StackDebug.Log(args, "BrowserProcess");
            CefSubProcess.RunCef(args);
        }
    }
}
