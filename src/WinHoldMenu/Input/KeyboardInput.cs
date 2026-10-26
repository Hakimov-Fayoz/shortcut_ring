using System.Runtime.InteropServices;
using WinHoldMenu.Interop;

namespace WinHoldMenu.Input;

internal static class KeyboardInput
{
    public static readonly UIntPtr OurInputMarker = (UIntPtr)0x57484D31u;

    private const uint KeyboardInputType = 1;
    private const uint ExtendedKeyFlag = 0x0001;
    private const uint KeyUpFlag = 0x0002;
    private const ushort UnassignedKey = 0xE8;
    private const ushort KeyV = 0x56;

    public static bool IsWinKey(uint key) => key == Native.VK_LWIN || key == Native.VK_RWIN;

    public static void PreventStartMenuOnWinRelease() => Send(Press(UnassignedKey), Release(UnassignedKey));

    public static void ReleaseKey(ushort key) => Send(Release(key));

    public static void PressCtrlV() =>
        Send(Press(Native.VK_CONTROL), Press(KeyV), Release(KeyV), Release(Native.VK_CONTROL));

    private static INPUT Press(ushort key) => CreateKeyEvent(key, isKeyUp: false);

    private static INPUT Release(ushort key) => CreateKeyEvent(key, isKeyUp: true);

    private static INPUT CreateKeyEvent(ushort key, bool isKeyUp)
    {
        uint flags = 0;
        if (isKeyUp) flags |= KeyUpFlag;
        if (IsWinKey(key)) flags |= ExtendedKeyFlag;

        var keyEvent = new KEYBDINPUT { wVk = key, dwFlags = flags, dwExtraInfo = OurInputMarker };
        return new INPUT { type = KeyboardInputType, u = new INPUTUNION { ki = keyEvent } };
    }

    private static void Send(params INPUT[] inputs) =>
        Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
}
