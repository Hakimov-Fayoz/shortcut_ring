using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace WinHoldMenu.UI;

internal static class MenuColors
{
    public static readonly SolidColorBrush Background = FromRgb(0x20, 0x23, 0x28);
    public static readonly SolidColorBrush HoveredRow = FromRgb(0x0F, 0x6E, 0x73);
    public static readonly SolidColorBrush Transparent = new(Colors.Transparent);
    public static readonly SolidColorBrush Text = FromRgb(0xE6, 0xE8, 0xEB);
    public static readonly SolidColorBrush PinnedText = FromRgb(0x7F, 0xD3, 0xCF);
    public static readonly SolidColorBrush MutedText = FromRgb(0x96, 0x9E, 0xAA);

    private static SolidColorBrush FromRgb(byte red, byte green, byte blue) =>
        new(ColorHelper.FromArgb(0xFF, red, green, blue));
}
