using System.Runtime.InteropServices;
using WinHoldMenu.Interop;

namespace WinHoldMenu.Shell;

internal static class TrayIconImage
{
    private readonly record struct Rgb(byte Red, byte Green, byte Blue);

    private static readonly Rgb Teal = new(0x0F, 0x6E, 0x73);
    private static readonly Rgb DarkClip = new(0x1A, 0x22, 0x30);
    private static readonly Rgb White = new(0xFF, 0xFF, 0xFF);

    private const int BytesPerPixel = 4;
    private const int MinimumSize = 16;

    public static IntPtr Create()
    {
        int size = Math.Max(MinimumSize, Native.GetSystemMetrics(Native.SM_CXSMICON));
        byte[] pixels = DrawClipboard(size);
        IntPtr icon = CreateIcon(pixels, size);

        return icon != IntPtr.Zero ? icon : Native.LoadIconW(IntPtr.Zero, Native.IDI_APPLICATION);
    }

    private static byte[] DrawClipboard(int size)
    {
        var pixels = new byte[size * size * BytesPerPixel];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double relativeX = (x + 0.5) / size;
                double relativeY = (y + 0.5) / size;
                if (ColorAt(relativeX, relativeY) is Rgb color)
                    SetPixel(pixels, (y * size + x) * BytesPerPixel, color);
            }
        }
        return pixels;
    }

    private static Rgb? ColorAt(double x, double y)
    {
        if (IsClip(x, y)) return DarkClip;
        if (!IsBoard(x, y)) return null;
        if (IsPaper(x, y) && !IsTextLine(x, y)) return White;
        return Teal;
    }

    private static bool IsClip(double x, double y) => Between(x, 0.34, 0.66) && Between(y, 0.04, 0.22);

    private static bool IsBoard(double x, double y) => IsInsideRoundedRectangle(x, y, 0.12, 0.12, 0.88, 0.96, 0.14);

    private static bool IsPaper(double x, double y) => Between(x, 0.24, 0.76) && Between(y, 0.28, 0.86);

    private static bool IsTextLine(double x, double y)
    {
        bool isFullLine = Between(y, 0.40, 0.47) || Between(y, 0.55, 0.62);
        bool isShortLine = Between(y, 0.70, 0.77) && x < 0.58;
        return (isFullLine || isShortLine) && Between(x, 0.32, 0.68);
    }

    private static bool IsInsideRoundedRectangle(double x, double y, double left, double top, double right, double bottom, double radius)
    {
        if (!Between(x, left, right) || !Between(y, top, bottom)) return false;

        double nearestX = Math.Clamp(x, left + radius, right - radius);
        double nearestY = Math.Clamp(y, top + radius, bottom - radius);
        return double.Hypot(x - nearestX, y - nearestY) <= radius;
    }

    private static bool Between(double value, double min, double max) => value >= min && value <= max;

    private static void SetPixel(byte[] pixels, int offset, Rgb color)
    {
        pixels[offset] = color.Blue;
        pixels[offset + 1] = color.Green;
        pixels[offset + 2] = color.Red;
        pixels[offset + 3] = 0xFF;
    }

    private static IntPtr CreateIcon(byte[] pixels, int size)
    {
        var bitmapInfo = BITMAPINFO.TopDown32Bit(size);
        IntPtr colorBitmap = Native.CreateDIBSection(IntPtr.Zero, ref bitmapInfo, Native.DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
        if (colorBitmap == IntPtr.Zero) return IntPtr.Zero;

        Marshal.Copy(pixels, 0, bits, pixels.Length);
        IntPtr maskBitmap = Native.CreateBitmap(size, size, 1, 1, IntPtr.Zero);

        var iconInfo = new ICONINFO { fIcon = 1, hbmMask = maskBitmap, hbmColor = colorBitmap };
        IntPtr icon = Native.CreateIconIndirect(ref iconInfo);

        Native.DeleteObject(colorBitmap);
        Native.DeleteObject(maskBitmap);
        return icon;
    }
}
