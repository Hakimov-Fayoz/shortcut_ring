using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace WinHoldMenu;

public static class Program
{
    [STAThread]
    private static int Main()
    {
        // single instance: second launch just exits
        using var mutex = new Mutex(true, @"Local\WinHoldMenu.SingleInstance", out bool isNew);
        if (!isNew) return 0;

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(args =>
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            _ = new App();
        });
        return 0;
    }
}
