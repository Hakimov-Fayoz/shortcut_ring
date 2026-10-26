using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace WinHoldMenu;

public static class Program
{
    private const string SingleInstanceName = @"Local\WinHoldMenu.SingleInstance";

    [STAThread]
    private static int Main()
    {
        using var mutex = new Mutex(true, SingleInstanceName, out bool isFirstInstance);
        if (!isFirstInstance) return 0;

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ => StartApp());
        return 0;
    }

    private static void StartApp()
    {
        var uiQueue = DispatcherQueue.GetForCurrentThread();
        SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(uiQueue));
        _ = new App();
    }
}
