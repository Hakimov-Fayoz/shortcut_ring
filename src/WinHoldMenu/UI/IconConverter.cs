using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using WinHoldMenu.Interop;

namespace WinHoldMenu.UI;

internal static class IconConverter
{
    private const int BytesPerPixel = 4;
    private const int AlphaOffset = 3;
    private const byte Opaque = 0xFF;

    public static WriteableBitmap? ToBitmap(IntPtr icon, int size)
    {
        if (icon == IntPtr.Zero) return null;

        byte[]? pixels = DrawIcon(icon, size);
        if (pixels is null) return null;

        if (HasNoTransparency(pixels)) MakeNonBlackPixelsOpaque(pixels);
        return CreateBitmap(pixels, size);
    }

    private static byte[]? DrawIcon(IntPtr icon, int size)
    {
        var bitmapInfo = BITMAPINFO.TopDown32Bit(size);
        IntPtr deviceContext = Native.CreateCompatibleDC(IntPtr.Zero);
        IntPtr bitmap = Native.CreateDIBSection(deviceContext, ref bitmapInfo, Native.DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero)
        {
            Native.DeleteDC(deviceContext);
            return null;
        }

        IntPtr previousBitmap = Native.SelectObject(deviceContext, bitmap);
        Native.DrawIconEx(deviceContext, 0, 0, icon, size, size, 0, IntPtr.Zero, Native.DI_NORMAL);
        Native.GdiFlush();

        var pixels = new byte[size * size * BytesPerPixel];
        Marshal.Copy(bits, pixels, 0, pixels.Length);

        Native.SelectObject(deviceContext, previousBitmap);
        Native.DeleteObject(bitmap);
        Native.DeleteDC(deviceContext);
        return pixels;
    }

    private static bool HasNoTransparency(byte[] pixels)
    {
        for (int i = AlphaOffset; i < pixels.Length; i += BytesPerPixel)
            if (pixels[i] != 0) return false;
        return true;
    }

    private static void MakeNonBlackPixelsOpaque(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += BytesPerPixel)
        {
            bool isBlack = pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0;
            if (!isBlack) pixels[i + AlphaOffset] = Opaque;
        }
    }

    private static WriteableBitmap CreateBitmap(byte[] pixels, int size)
    {
        var bitmap = new WriteableBitmap(size, size);
        using (Stream stream = bitmap.PixelBuffer.AsStream())
            stream.Write(pixels, 0, pixels.Length);

        bitmap.Invalidate();
        return bitmap;
    }
}
