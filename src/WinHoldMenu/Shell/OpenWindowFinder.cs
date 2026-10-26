using System.Text;
using WinHoldMenu.Interop;

namespace WinHoldMenu.Shell;

internal static class OpenWindowFinder
{
    private const int IconRequestTimeoutMs = 50;
    private const int MaxClassNameLength = 256;
    private const int MaxPathLength = 1024;

    private static readonly int[] IconSizesToTry = { Native.ICON_SMALL2, Native.ICON_SMALL, Native.ICON_BIG };

    private static readonly HashSet<string> DesktopAndTaskbarClasses = new()
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
    };

    public static List<OpenWindow> FindMostRecent(int maxCount)
    {
        var windows = new List<OpenWindow>();
        Native.EnumWindows((handle, _) =>
        {
            if (IsUserWindow(handle)) windows.Add(Describe(handle));
            return windows.Count < maxCount;
        }, IntPtr.Zero);
        return windows;
    }

    private static bool IsUserWindow(IntPtr handle)
    {
        if (!Native.IsWindowVisible(handle)) return false;
        if (Native.GetWindowTextLengthW(handle) == 0) return false;
        if (IsToolOrOwnedWindow(handle)) return false;
        if (IsCloaked(handle)) return false;
        if (BelongsToThisApp(handle)) return false;
        return !DesktopAndTaskbarClasses.Contains(GetClassName(handle));
    }

    private static bool IsToolOrOwnedWindow(IntPtr handle)
    {
        long style = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
        bool forcedIntoTaskbar = (style & Native.WS_EX_APPWINDOW) != 0;
        if (forcedIntoTaskbar) return false;

        bool isToolWindow = (style & Native.WS_EX_TOOLWINDOW) != 0;
        bool hasOwner = Native.GetWindow(handle, Native.GW_OWNER) != IntPtr.Zero;
        return isToolWindow || hasOwner;
    }

    private static bool IsCloaked(IntPtr handle) =>
        Native.DwmGetWindowAttribute(handle, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static bool BelongsToThisApp(IntPtr handle)
    {
        Native.GetWindowThreadProcessId(handle, out uint processId);
        return processId == Environment.ProcessId;
    }

    private static OpenWindow Describe(IntPtr handle) =>
        new(handle, GetTitle(handle), GetAppName(handle), GetIcon(handle));

    private static string GetTitle(IntPtr handle)
    {
        var title = new StringBuilder(Native.GetWindowTextLengthW(handle) + 1);
        Native.GetWindowTextW(handle, title, title.Capacity);
        return title.ToString();
    }

    private static string GetClassName(IntPtr handle)
    {
        var className = new StringBuilder(MaxClassNameLength);
        Native.GetClassNameW(handle, className, className.Capacity);
        return className.ToString();
    }

    private static string GetAppName(IntPtr handle)
    {
        Native.GetWindowThreadProcessId(handle, out uint processId);
        IntPtr process = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero) return "";

        try
        {
            var path = new StringBuilder(MaxPathLength);
            uint length = (uint)path.Capacity;
            return Native.QueryFullProcessImageNameW(process, 0, path, ref length)
                ? Path.GetFileNameWithoutExtension(path.ToString())
                : "";
        }
        finally
        {
            Native.CloseHandle(process);
        }
    }

    private static IntPtr GetIcon(IntPtr handle)
    {
        foreach (int iconSize in IconSizesToTry)
        {
            IntPtr icon = AskWindowForIcon(handle, iconSize);
            if (icon != IntPtr.Zero) return icon;
        }

        IntPtr classIcon = Native.GetClassLongPtr(handle, Native.GCLP_HICONSM);
        if (classIcon == IntPtr.Zero) classIcon = Native.GetClassLongPtr(handle, Native.GCLP_HICON);
        if (classIcon != IntPtr.Zero) return classIcon;

        return Native.LoadIconW(IntPtr.Zero, Native.IDI_APPLICATION);
    }

    private static IntPtr AskWindowForIcon(IntPtr handle, int iconSize)
    {
        Native.SendMessageTimeoutW(handle, Native.WM_GETICON, iconSize, IntPtr.Zero,
            Native.SMTO_ABORTIFHUNG, IconRequestTimeoutMs, out IntPtr icon);
        return icon;
    }
}
