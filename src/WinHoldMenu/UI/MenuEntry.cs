using WinHoldMenu.Shell;

namespace WinHoldMenu.UI;

internal abstract record MenuEntry;

internal sealed record TextEntry(string Text, bool IsPinned) : MenuEntry;

internal sealed record WindowEntry(OpenWindow Window) : MenuEntry;
