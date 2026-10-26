using System.Runtime.InteropServices;

namespace WinHoldMenu.Interop;

internal static partial class Native
{
    public const uint WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205;

    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    public const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;

    public const uint TPM_RIGHTBUTTON = 0x0002, TPM_BOTTOMALIGN = 0x0020, TPM_NONOTIFY = 0x0080, TPM_RETURNCMD = 0x0100;
    public const uint MF_STRING = 0x0000, MF_CHECKED = 0x0008, MF_SEPARATOR = 0x0800;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATA data);

    [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] public static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr menu);
}
