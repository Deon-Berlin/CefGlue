using System;
using System.Collections.Generic;
using System.Linq;
using Xilium.CefGlue.Common.Events;
using Xilium.CefGlue.Common.Shared.RendererProcessCommunication;

namespace Xilium.CefGlue.Common.ObjectBinding
{
    internal class NativeObjectRegistry : IDisposable
    {
        private CefBrowser _browser;
        private readonly Dictionary<string, NativeObject> _registeredObjects = new Dictionary<string, NativeObject>();
        private readonly object _registrationSyncRoot = new object();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="name"></param>
        /// <returns>True if the object was successfully registered, false if the object was already registered before.</returns>
        public bool Register(object obj, string name, MethodCallHandler methodHandler = null)
        {
            if (_registeredObjects.ContainsKey(name))
            {
                return false;
            }
            
            var nativeObj = new NativeObject(name, obj, methodHandler);

            lock (_registrationSyncRoot)
            {
                if (_registeredObjects.ContainsKey(name))
                {
                    // check gain, might have been registered meanwhile
                    return false;
                }

                _registeredObjects.Add(name, nativeObj);

                if (_browser != null)
                {
                    SendRegistrationMessage(nativeObj);
                }

                return true;
            }
        }

        public void Unregister(string name)
        {
            lock (_registrationSyncRoot)
            {
                _registeredObjects.Remove(name);

                if (_browser != null)
                {
                    var message = new Messages.NativeObjectUnregistrationRequest()
                    {
                        ObjectName = name,
                    };

                    var cefMessage = message.ToCefProcessMessage();
                    // TODO target main frame?
                    _browser.GetMainFrame().SendProcessMessage(CefProcessId.Renderer, cefMessage);
                }
            }
        }

        public void SetBrowser(CefBrowser browser)
        {
            lock (_registrationSyncRoot)
            {
                _browser = browser;
                foreach (var obj in _registeredObjects.Values)
                {
                    SendRegistrationMessage(obj);
                }
            }
        }

        public NativeObject Get(string name)
        {
            _registeredObjects.TryGetValue(name, out var obj);
            return obj;
        }

        /// <summary>
        /// (Re)sends the registration of all objects to the given (now live) main frame.
        /// This is required because a registration message sent (via <see cref="Register"/> /
        /// <see cref="SetBrowser"/>) to the current main frame is dropped by CEF when that frame
        /// has no live render process yet (e.g. the initial empty frame before the first
        /// navigation), and because a cross-process navigation creates a fresh render process that
        /// never received the original registration.
        ///
        /// Called both when the render side reports a context was created and — critically — right
        /// before <c>LoadEnd</c> is raised to consumers. Consumers (and the native-object tests)
        /// script the freshly loaded page in response to <c>LoadEnd</c>; sending the registration
        /// here, on the same browser-local signal and before that event, guarantees the
        /// registration message reaches the render frame before any browser→render script the
        /// consumer issues (both target the same frame and are delivered in send order). This
        /// closes a race where re-sending only on the asynchronous <c>JsContextCreated</c> IPC
        /// could arrive after the consumer's script had already run (giving "undefined" object).
        /// </summary>
        public void SendRegistrations(CefFrame frame)
        {
            lock (_registrationSyncRoot)
            {
                foreach (var obj in _registeredObjects.Values)
                {
                    SendRegistrationMessage(obj, frame);
                }
            }
        }

        private void SendRegistrationMessage(NativeObject obj, CefFrame frame = null)
        {
            var message = new Messages.NativeObjectRegistrationRequest()
            {
                ObjectName = obj.Name,
                MethodsNames = obj.MethodsNames.ToArray()
            };

            var cefMessage = message.ToCefProcessMessage();
            frame ??= _browser.GetMainFrame();
            frame.SendProcessMessage(CefProcessId.Renderer, cefMessage);
        }

        public void Dispose()
        {
            lock (_registrationSyncRoot)
            {
                _registeredObjects.Clear();
            }

            _browser = null;
        }
    }
}
