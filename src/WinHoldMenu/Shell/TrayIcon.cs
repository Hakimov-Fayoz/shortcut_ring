using System.Runtime.InteropServices;
using WinHoldMenu.Input;
using WinHoldMenu.Interop;

namespace WinHoldMenu.Shell;

internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;

    private readonly IntPtr _ownerWindow;
    private readonly uint _clickMessage;
    private readonly IntPtr _icon = TrayIconImage.Create();
    private string _tooltip;

    public TrayIcon(IntPtr ownerWindow, uint clickMessage, string tooltip)
    {
        _ownerWindow = ownerWindow;
        _clickMessage = clickMessage;
        _tooltip = tooltip;
    }

    public void Show() => Send(Native.NIM_ADD);

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        Send(Native.NIM_MODIFY);
    }

    public TrayCommand ShowMenu(params TrayMenuItem[] items)
    {
        IntPtr menu = Native.CreatePopupMenu();
        foreach (TrayMenuItem item in items)
            Native.AppendMenuW(menu, FlagsFor(item), (UIntPtr)(uint)item.Command, item.IsSeparator ? null : item.Text);

        POINT cursor = MouseCursor.GetPosition();
        Native.SetForegroundWindow(_ownerWindow);
        int chosen = Native.TrackPopupMenu(menu,
            Native.TPM_RETURNCMD | Native.TPM_NONOTIFY | Native.TPM_RIGHTBUTTON | Native.TPM_BOTTOMALIGN,
            cursor.X, cursor.Y, 0, _ownerWindow, IntPtr.Zero);
        Native.PostMessageW(_ownerWindow, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        Native.DestroyMenu(menu);

        return (TrayCommand)chosen;
    }

    private static uint FlagsFor(TrayMenuItem item)
    {
        if (item.IsSeparator) return Native.MF_SEPARATOR;
        return item.IsChecked ? Native.MF_STRING | Native.MF_CHECKED : Native.MF_STRING;
    }

    private void Send(uint command)
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _ownerWindow,
            uID = IconId,
            uFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP,
            uCallbackMessage = _clickMessage,
            hIcon = _icon,
            szTip = _tooltip,
            szInfo = "",
            szInfoTitle = "",
        };
        Native.Shell_NotifyIconW(command, ref data);
    }

    public void Dispose()
    {
        Send(Native.NIM_DELETE);
        Native.DestroyIcon(_icon);
    }
}
