using System.Runtime.InteropServices;
using WinHoldMenu.Interop;

namespace WinHoldMenu.Clipboard;

internal static class ClipboardText
{
    private const int OpenAttempts = 10;
    private const int DelayBetweenAttemptsMs = 15;

    private static readonly uint SecretFromPasswordManager = Native.RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint HiddenFromClipboardViewers = Native.RegisterClipboardFormatW("Clipboard Viewer Ignore");
    private static readonly uint AllowedInHistoryFlag = Native.RegisterClipboardFormatW("CanIncludeInClipboardHistory");

    public static uint ChangeNumber => Native.GetClipboardSequenceNumber();

    public static string? Read(IntPtr ownerWindow)
    {
        if (!Has(Native.CF_UNICODETEXT)) return null;
        if (Has(SecretFromPasswordManager) || Has(HiddenFromClipboardViewers)) return null;
        if (!TryOpen(ownerWindow)) return null;

        try
        {
            if (Has(AllowedInHistoryFlag) && !ReadFlag(AllowedInHistoryFlag)) return null;
            return ReadUnicodeText();
        }
        finally
        {
            Native.CloseClipboard();
        }
    }

    public static bool Write(IntPtr ownerWindow, string text)
    {
        if (!TryOpen(ownerWindow)) return false;

        try
        {
            if (!Native.EmptyClipboard()) return false;

            IntPtr memory = CopyToGlobalMemory(text);
            if (memory == IntPtr.Zero) return false;

            if (Native.SetClipboardData(Native.CF_UNICODETEXT, memory) != IntPtr.Zero) return true;

            Native.GlobalFree(memory);
            return false;
        }
        finally
        {
            Native.CloseClipboard();
        }
    }

    private static bool Has(uint format) => format != 0 && Native.IsClipboardFormatAvailable(format);

    private static bool TryOpen(IntPtr ownerWindow)
    {
        for (int attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (Native.OpenClipboard(ownerWindow)) return true;
            Thread.Sleep(DelayBetweenAttemptsMs);
        }
        return false;
    }

    private static string? ReadUnicodeText()
    {
        IntPtr handle = Native.GetClipboardData(Native.CF_UNICODETEXT);
        IntPtr text = Native.GlobalLock(handle);
        if (text == IntPtr.Zero) return null;

        try
        {
            return Marshal.PtrToStringUni(text);
        }
        finally
        {
            Native.GlobalUnlock(handle);
        }
    }

    private static bool ReadFlag(uint format)
    {
        IntPtr handle = Native.GetClipboardData(format);
        IntPtr value = Native.GlobalLock(handle);
        if (value == IntPtr.Zero) return true;

        try
        {
            return Marshal.ReadInt32(value) != 0;
        }
        finally
        {
            Native.GlobalUnlock(handle);
        }
    }

    private static IntPtr CopyToGlobalMemory(string text)
    {
        char[] characters = (text + '\0').ToCharArray();
        int sizeInBytes = characters.Length * sizeof(char);

        IntPtr memory = Native.GlobalAlloc(Native.GMEM_MOVEABLE, (UIntPtr)sizeInBytes);
        IntPtr destination = Native.GlobalLock(memory);
        if (destination == IntPtr.Zero)
        {
            Native.GlobalFree(memory);
            return IntPtr.Zero;
        }

        Marshal.Copy(characters, 0, destination, characters.Length);
        Native.GlobalUnlock(memory);
        return memory;
    }
}
