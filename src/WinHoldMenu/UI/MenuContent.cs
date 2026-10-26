using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace WinHoldMenu.UI;

internal sealed class MenuContent
{
    public const double TextColumnWidth = 300;
    public const double WindowColumnWidth = 280;
    public const double ColumnGap = 12;
    public const double OuterPadding = 10;
    public const double HeaderHeight = 28;
    public const double TotalWidth = OuterPadding * 2 + TextColumnWidth + ColumnGap + WindowColumnWidth;

    public static readonly Point EmptySpotBetweenHeaders =
        new(OuterPadding + TextColumnWidth + ColumnGap / 2, OuterPadding + HeaderHeight / 2);

    private const double IconSize = 20;
    private const int MaxPreviewLength = 200;
    private const string PinnedMark = "★ ";

    private readonly List<MenuRow> _rows = new();

    public Grid View { get; }
    public IReadOnlyList<MenuRow> Rows => _rows;

    public MenuContent(IReadOnlyList<TextEntry> texts, IReadOnlyList<WindowEntry> windows, double scale)
    {
        var textColumn = CreateColumn("Буфер обмена", texts.Select(CreateTextRow), "Пусто — скопируйте что-нибудь");
        var windowColumn = CreateColumn("Окна", windows.Select(window => CreateWindowRow(window, scale)), "Других окон нет");
        Grid.SetColumn(windowColumn, 1);

        View = new Grid
        {
            Padding = new Thickness(OuterPadding),
            ColumnSpacing = ColumnGap,
            Background = MenuColors.Background,
        };
        View.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TextColumnWidth) });
        View.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WindowColumnWidth) });
        View.Children.Add(textColumn);
        View.Children.Add(windowColumn);
    }

    private StackPanel CreateColumn(string title, IEnumerable<MenuRow> rows, string textWhenEmpty)
    {
        var column = new StackPanel { Spacing = 2 };
        column.Children.Add(CreateHeader(title));

        foreach (MenuRow row in rows)
        {
            _rows.Add(row);
            column.Children.Add(row.View);
        }

        bool hasNoRows = column.Children.Count == 1;
        if (hasNoRows) column.Children.Add(CreateHint(textWhenEmpty));

        return column;
    }

    private static TextBlock CreateHeader(string title) => new()
    {
        Text = title,
        Height = HeaderHeight,
        Padding = new Thickness(10, 6, 10, 0),
        FontSize = 12,
        Foreground = MenuColors.MutedText,
    };

    private static TextBlock CreateHint(string text) => new()
    {
        Text = text,
        Padding = new Thickness(10, 7, 10, 7),
        Foreground = MenuColors.MutedText,
    };

    private static MenuRow CreateTextRow(TextEntry entry)
    {
        string preview = ToSingleLine(entry.Text);
        var label = new TextBlock
        {
            Text = entry.IsPinned ? PinnedMark + preview : preview,
            Foreground = entry.IsPinned ? MenuColors.PinnedText : MenuColors.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        return new MenuRow(entry, label);
    }

    private static MenuRow CreateWindowRow(WindowEntry entry, double scale)
    {
        int iconSizeInPixels = (int)Math.Round(IconSize * scale);
        var icon = new Image
        {
            Width = IconSize,
            Height = IconSize,
            VerticalAlignment = VerticalAlignment.Center,
            Source = IconConverter.ToBitmap(entry.Window.Icon, iconSizeInPixels),
        };

        var title = new TextBlock
        {
            Text = entry.Window.Title,
            Foreground = MenuColors.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        var appName = new TextBlock
        {
            Text = entry.Window.AppName,
            FontSize = 12,
            Foreground = MenuColors.MutedText,
        };

        var titles = new StackPanel();
        titles.Children.Add(title);
        titles.Children.Add(appName);

        var layout = new Grid { ColumnSpacing = 10 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(titles, 1);
        layout.Children.Add(icon);
        layout.Children.Add(titles);

        return new MenuRow(entry, layout);
    }

    private static string ToSingleLine(string text)
    {
        string singleLine = Regex.Replace(text.Trim(), @"\s+", " ");
        return singleLine.Length <= MaxPreviewLength ? singleLine : singleLine[..MaxPreviewLength];
    }
}
