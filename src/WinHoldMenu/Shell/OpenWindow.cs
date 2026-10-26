namespace WinHoldMenu.Shell;

internal sealed record OpenWindow(IntPtr Handle, string Title, string AppName, IntPtr Icon);
