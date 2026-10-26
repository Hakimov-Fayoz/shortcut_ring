using System.Runtime.InteropServices;
using WinHoldMenu.Interop;

namespace WinHoldMenu.Input;

/// <summary>Synthetic input + window activation helpers.</summary>
internal static class Actions
{
    /// <summary>Tag on every key event we inject, so our own hook ignores them.</summary>
    public static readonly UIntPtr Marker = (UIntPtr)0x57484D31u;

    private static INPUT Key(ushort vk, bool up)
    {
        uint flags = up ? 2u : 0u;                                  // KEYEVENTF_KEYUP
        if (vk == Native.VK_LWIN || vk == Native.VK_RWIN) flags |= 1u; // KEYEVENTF_EXTENDEDKEY
        return new INPUT
        {
            type = 1, // INPUT_KEYBOARD
            u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags, dwExtraInfo = Marker } },
        };
    }

    private static void Send(params INPUT[] inputs) =>
        Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

    /// <summary>
    /// Unassigned key tap while Win is held: the shell then treats Win as part of a
    /// combination and does not open the Start menu on release.
    /// </summary>
    public static void InjectMask() => Send(Key(0xE8, false), Key(0xE8, true));

    /// <summary>Virtually release Win so Ctrl+V does not become Win+Ctrl+V.</summary>
    public static void SendWinUp(uint vk) => Send(Key((ushort)vk, true));

    public static POINT CursorPos()
    {
        Native.GetCursorPos(out var p);
        return p;
    }

    public static void BringToFront(IntPtr hwnd)
    {
        if (!Native.IsWindow(hwnd)) return;
        if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);

        // Borrow the foreground thread's input state so SetForegroundWindow is allowed.
        IntPtr fg = Native.GetForegroundWindow();
        uint fgThread = fg != IntPtr.Zero ? Native.GetWindowThreadProcessId(fg, out _) : 0;
        uint me = Native.GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != me && Native.AttachThreadInput(me, fgThread, true);

        Native.BringWindowToTop(hwnd);
        Native.SetForegroundWindow(hwnd);

        if (attached) Native.AttachThreadInput(me, fgThread, false);
    }
}
