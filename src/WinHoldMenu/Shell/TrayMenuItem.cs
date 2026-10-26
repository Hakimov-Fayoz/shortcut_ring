namespace WinHoldMenu.Shell;

internal enum TrayCommand : uint
{
    None = 0,
    ToggleEnabled,
    ToggleAutostart,
    OpenSettings,
    Exit,
}

internal sealed record TrayMenuItem(TrayCommand Command, string Text, bool IsChecked = false)
{
    public static readonly TrayMenuItem Separator = new(TrayCommand.None, "");

    public bool IsSeparator => Command == TrayCommand.None;
}
