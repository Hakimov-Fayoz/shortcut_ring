using System.Runtime.InteropServices;

namespace WinHoldMenu.Interop;

internal static partial class Native
{
    public const int ICON_SMALL = 0, ICON_BIG = 1, ICON_SMALL2 = 2;
    public const int GCLP_HICON = -14, GCLP_HICONSM = -34;
    public const int IDI_APPLICATION = 32512;
    public const int SM_CXSMICON = 49;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public const uint DI_NORMAL = 0x0003;
    public const uint DIB_RGB_COLORS = 0;

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] public static extern IntPtr GetClassLongPtr(IntPtr h, int index);
    [DllImport("user32.dll")] public static extern IntPtr LoadIconW(IntPtr hInst, IntPtr name);
    [DllImport("user32.dll")] public static extern IntPtr CreateIconIndirect(ref ICONINFO info);
    [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy, uint step, IntPtr brush, uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);

    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateBitmap(int w, int h, uint planes, uint bitCount, IntPtr bits);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool GdiFlush();
}
