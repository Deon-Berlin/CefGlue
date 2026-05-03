using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Xilium.CefGlue.Common.Extensions
{
    public static class AsyncExtensions
    {
        extension(CefCookieManager)
        {
            public static async Task<CefCookieManager> GetGlobalAsync()
            {
                var tcs = new TaskCompletionCallback();
                var cookieManager = CefCookieManager.GetGlobal(tcs);
                return await tcs.Task ? cookieManager : null;
            }
        }
    }

    public class TaskCompletionCallback : CefCompletionCallback
    {
        private readonly TaskCompletionSource<bool> _completionSource = new();

        protected override void OnComplete() => _completionSource.TrySetResult(true);

        public Task<bool> Task => _completionSource.Task;

    }
}
