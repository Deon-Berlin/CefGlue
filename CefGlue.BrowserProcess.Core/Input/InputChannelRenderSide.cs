using Xilium.CefGlue;

namespace Xilium.CefGlue.BrowserProcess.Input
{
    /// <summary>
    /// Injects <c>window.__cefSendInput(json)</c> into the page. When JS calls it, the JSON input
    /// payload is posted to the browser process as a "__cefInput" message (arg 0 = json), where the
    /// app routes it to the target offscreen browser. Mirror of the frame-delivery channel.
    /// </summary>
    internal sealed class InputChannelRenderSide : CefV8Handler
    {
        public const string FunctionName = "__cefSendInput";
        public const string MessageName = "__cefInput";

        /// <summary>Install the global function into a (main-frame) V8 context.</summary>
        public void Install(CefV8Context context)
        {
            if (!context.Enter()) return;
            try
            {
                var global = context.GetGlobal();
                global.SetValue(FunctionName, CefV8Value.CreateFunction(FunctionName, this));
            }
            finally
            {
                context.Exit();
            }
        }

        protected override bool Execute(string name, CefV8Value obj, CefV8Value[] arguments,
            out CefV8Value returnValue, out string exception)
        {
            returnValue = null;
            exception = null;

            if (arguments.Length >= 1)
            {
                var json = arguments[0].GetStringValue();
                var frame = CefV8Context.GetCurrentContext()?.GetFrame();
                if (frame != null && json != null)
                {
                    using var message = CefProcessMessage.Create(MessageName);
                    using (var args = message.Arguments)
                    {
                        args.SetString(0, json);
                    }
                    frame.SendProcessMessage(CefProcessId.Browser, message);
                }
            }
            return true;
        }
    }
}
