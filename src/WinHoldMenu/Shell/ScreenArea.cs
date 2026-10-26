using System.Runtime.InteropServices;
using WinHoldMenu.Interop;

namespace WinHoldMenu.Shell;

internal readonly record struct ScreenArea(RECT WorkArea, double Scale)
{
    private const double StandardDpi = 96;

    public static ScreenArea At(POINT point)
    {
        IntPtr monitor = Native.MonitorFromPoint(point, Native.MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        Native.GetMonitorInfoW(monitor, ref info);

        bool hasDpi = Native.GetDpiForMonitor(monitor, Native.MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0;
        return new ScreenArea(info.rcWork, hasDpi ? dpi / StandardDpi : 1.0);
    }
}
