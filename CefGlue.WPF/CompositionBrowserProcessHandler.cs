using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Xilium.CefGlue.Common.Handlers;

namespace Xilium.CefGlue.WPF;

public class CompositionBrowserProcessHandler : BrowserProcessHandler
{
    private bool _isScheduled;

    protected override void OnScheduleMessagePumpWork(long delayMs)
    {
        if (_isScheduled) return;
        
        _isScheduled = true;
        Application.Current?.Dispatcher.Invoke(() =>
        {
            CompositionTarget.Rendering += OnRendering;    
        });
        return;

        void OnRendering(object sender, EventArgs e)
        {
            CefRuntime.DoMessageLoopWork();
        }
    }
}
