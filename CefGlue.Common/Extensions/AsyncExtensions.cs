using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Xilium.CefGlue.Common.Extensions
{
    public static class AsyncExtensions
    {
        extension(CefCookieManager manager)
        {
            public static async Task<CefCookieManager> GetGlobalAsync()
            {
                var tcs = new TaskCompletionCallback();
                var cookieManager = CefCookieManager.GetGlobal(tcs);
                return await tcs.Task ? cookieManager : null;
            }

            public async Task<IEnumerable<Cookie>> GetAllCookiesAsync()
            {
                var visitor = new TaskCookieVisitor();
                manager.VisitAllCookies(visitor);
                return await visitor.Task;
            }

            public async Task<IEnumerable<Cookie>> GetCookiesAsync(string url, bool httpOnly = true)
            {
                var visitor = new TaskCookieVisitor();
                manager.VisitUrlCookies(url, httpOnly, visitor);
                return await visitor.Task;
            }

            public Task<bool> SetCookieAsync(Cookie cookie)
            {
                var callback = new TaskCefSetCookieCallback();
                manager.SetCookie($"https://{cookie.Domain.TrimStart('.')}", FromCookie(cookie), callback);
                return callback.Task;
            }

            public async Task SetCookiesAsync(IEnumerable<Cookie> cookies)
            {
                foreach (var cookie in cookies)
                {
                    await manager.SetCookieAsync(cookie);
                }
            }
        }

        extension(CefCookie cookie)
        {
            public static CefCookie FromCookie(Cookie netCookie)
            {
                CefBaseTime.FromUtcExploded(new CefTime(netCookie.Expires.ToUniversalTime()), out var expires);
                return new CefCookie
                {
                    Name = netCookie.Name,
                    Value = netCookie.Value,
                    Expires = expires,
                    Domain = netCookie.Domain,
                    Path = netCookie.Path,
                    HttpOnly = netCookie.HttpOnly,
                    Secure = netCookie.Secure
                };
            }

            public Cookie ToCookie()
            {
                return new Cookie
                {
                    Name = cookie.Name,
                    Value = cookie.Value,
                    Domain = cookie.Domain,
                    Path = cookie.Path,
                    Expires = cookie.Expires.ToDateTime(),
                    Secure = cookie.Secure,
                    HttpOnly = cookie.HttpOnly
                };
            }
        }

        extension(CefBaseTime? baseTime)
        {
            public DateTime ToDateTime()
            {
                if (baseTime.HasValue && baseTime.Value.UtcExplode(out var exploded))
                { 
                    return exploded.ToDateTime();
                }
                
                return DateTime.MaxValue;
            }
        }
            
        
        extension(CefFrame frame)
        {
            public Task<string> GetSourceAsync()
            {
                var visitor = new TaskStringVisitor();
                frame.GetSource(visitor);
                return visitor.Task;
            }
        }
    }

    public class TaskCompletionCallback : CefCompletionCallback
    {
        private readonly TaskCompletionSource<bool> _completionSource = new();

        protected override void OnComplete() => _completionSource.TrySetResult(true);

        public Task<bool> Task => _completionSource.Task;
    }

    public class TaskCefSetCookieCallback : CefSetCookieCallback
    {
        private readonly TaskCompletionSource<bool> _completionSource = new();
        
        protected override void OnComplete(bool success)
        {
            _completionSource.TrySetResult(success);
        }

        public Task<bool> Task => _completionSource.Task;
    }

    public class TaskCookieVisitor : CefCookieVisitor
    {
        private readonly TaskCompletionSource<List<Cookie>> _taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<Cookie> _cookies = new();

        protected override bool Visit(CefCookie cookie, int count, int total, out bool delete)
        {
            _cookies.Add(cookie.ToCookie());
            if (count == total - 1)
            {
                _taskCompletionSource.TrySetResult(_cookies);
            }

            delete = false;
            return true;
        }

        public Task<List<Cookie>> Task => _taskCompletionSource.Task;
    }

    public class TaskStringVisitor : CefStringVisitor
    {
        private readonly TaskCompletionSource<string> _taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override void Visit(string value)
        {
            _taskCompletionSource.TrySetResult(value);
        }

        public Task<string> Task => _taskCompletionSource.Task;
    }
}
