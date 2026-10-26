using WinHoldMenu.Interop;

namespace WinHoldMenu.Shell;

internal static class WindowActivator
{
    public static void BringToFront(IntPtr window)
    {
        if (!Native.IsWindow(window)) return;
        if (Native.IsIconic(window)) Native.ShowWindow(window, Native.SW_RESTORE);

        uint ourThread = Native.GetCurrentThreadId();
        uint foregroundThread = GetForegroundThread();
        bool sharesInputWithForeground = foregroundThread != 0
            && foregroundThread != ourThread
            && Native.AttachThreadInput(ourThread, foregroundThread, true);

        Native.BringWindowToTop(window);
        Native.SetForegroundWindow(window);

        if (sharesInputWithForeground) Native.AttachThreadInput(ourThread, foregroundThread, false);
    }

    private static uint GetForegroundThread()
    {
        IntPtr foreground = Native.GetForegroundWindow();
        return foreground == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(foreground, out _);
    }
}
