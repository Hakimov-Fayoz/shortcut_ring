using WinHoldMenu.Interop;

namespace WinHoldMenu.Input;

internal static class MouseCursor
{
    public static POINT GetPosition()
    {
        Native.GetCursorPos(out POINT position);
        return position;
    }
}
