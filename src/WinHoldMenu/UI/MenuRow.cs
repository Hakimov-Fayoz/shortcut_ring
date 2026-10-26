using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinHoldMenu.UI;

internal sealed class MenuRow
{
    public MenuEntry Entry { get; }
    public Border View { get; }

    public event Action<MenuRow>? PointerMoved;
    public event Action<MenuRow>? PointerLeft;
    public event Action<MenuRow>? Clicked;

    public MenuRow(MenuEntry entry, UIElement content)
    {
        Entry = entry;
        View = new Border
        {
            Child = content,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7),
            Background = MenuColors.Transparent,
        };

        View.PointerMoved += (_, _) => PointerMoved?.Invoke(this);
        View.PointerExited += (_, _) => PointerLeft?.Invoke(this);
        View.PointerPressed += (_, _) => Clicked?.Invoke(this);
    }

    public void Highlight() => View.Background = MenuColors.HoveredRow;

    public void RemoveHighlight() => View.Background = MenuColors.Transparent;
}
