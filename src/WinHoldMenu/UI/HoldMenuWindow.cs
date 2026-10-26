using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Graphics;
using WinHoldMenu.Input;
using WinHoldMenu.Interop;
using WinHoldMenu.Shell;
using WinRT.Interop;

namespace WinHoldMenu.UI;

internal sealed class HoldMenuWindow
{
    private const int OffScreen = -32000;
    private const int PixelsToMoveBeforeHoverCounts = 8;

    private readonly Window _window = new();
    private readonly IntPtr _handle;
    private readonly DispatcherQueueTimer _hoverTimer;

    private POINT _cursorWhenOpened;
    private bool _cursorHasMoved;
    private bool _chooseOnHover;
    private MenuRow? _hoveredRow;

    public event Action<MenuEntry>? EntryChosen;

    public bool IsOpen { get; private set; }
    public MenuEntry? HoveredEntry => _hoveredRow?.Entry;

    public HoldMenuWindow()
    {
        _handle = WindowNative.GetWindowHandle(_window);

        _hoverTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _hoverTimer.IsRepeating = false;
        _hoverTimer.Tick += (_, _) => OnHoverTimeReached();

        TurnIntoPopup();
        PrepareOffScreen();
    }

    public void Show(IReadOnlyList<TextEntry> texts, IReadOnlyList<WindowEntry> windows, bool chooseOnHover, int hoverDelayMs)
    {
        _cursorWhenOpened = MouseCursor.GetPosition();
        _cursorHasMoved = false;
        _hoveredRow = null;
        _chooseOnHover = chooseOnHover;
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(hoverDelayMs);

        ScreenArea screen = ScreenArea.At(_cursorWhenOpened);
        var content = new MenuContent(texts, windows, screen.Scale);
        foreach (MenuRow row in content.Rows) ListenTo(row);

        _window.Content = content.View;
        PlaceAroundCursor(content.View, screen);
        IsOpen = true;
    }

    public void Hide()
    {
        _hoverTimer.Stop();
        _hoveredRow = null;
        IsOpen = false;

        Native.ShowWindow(_handle, Native.SW_HIDE);
        _window.Content = null;
    }

    private void TurnIntoPopup()
    {
        _window.AppWindow.IsShownInSwitchers = false;
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        long style = Native.GetWindowLongPtr(_handle, Native.GWL_EXSTYLE).ToInt64();
        style |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
        style &= ~Native.WS_EX_APPWINDOW;
        Native.SetWindowLongPtr(_handle, Native.GWL_EXSTYLE, new IntPtr(style));

        int roundCorners = Native.DWMWCP_ROUND;
        Native.DwmSetWindowAttribute(_handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref roundCorners, sizeof(int));
    }

    private void PrepareOffScreen()
    {
        _window.AppWindow.MoveAndResize(new RectInt32(OffScreen, OffScreen, 1, 1));
        _window.Activate();
        Native.ShowWindow(_handle, Native.SW_HIDE);
    }

    private void PlaceAroundCursor(FrameworkElement view, ScreenArea screen)
    {
        view.Measure(new Size(MenuContent.TotalWidth, double.PositiveInfinity));

        int width = ToPixels(MenuContent.TotalWidth, screen.Scale);
        int height = ToPixels(view.DesiredSize.Height, screen.Scale);
        int left = _cursorWhenOpened.X - ToPixels(MenuContent.EmptySpotBetweenHeaders.X, screen.Scale);
        int top = _cursorWhenOpened.Y - ToPixels(MenuContent.EmptySpotBetweenHeaders.Y, screen.Scale);

        RECT workArea = screen.WorkArea;
        left = KeepInside(left, width, workArea.Left, workArea.Right);
        top = KeepInside(top, height, workArea.Top, workArea.Bottom);

        Native.SetWindowPos(_handle, Native.HWND_TOPMOST, left, top, width, height,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    private static int ToPixels(double size, double scale) => (int)Math.Ceiling(size * scale);

    private static int KeepInside(int start, int length, int min, int max) =>
        Math.Clamp(start, min, Math.Max(min, max - length));

    private void ListenTo(MenuRow row)
    {
        row.PointerMoved += OnRowPointerMoved;
        row.PointerLeft += OnRowPointerLeft;
        row.Clicked += clickedRow => Choose(clickedRow.Entry);
    }

    private void OnRowPointerMoved(MenuRow row)
    {
        if (!_cursorHasMoved && !CursorMovedSinceOpening()) return;
        _cursorHasMoved = true;

        if (row == _hoveredRow) return;

        _hoveredRow?.RemoveHighlight();
        _hoveredRow = row;
        row.Highlight();

        if (_chooseOnHover) RestartHoverTimer();
    }

    private void OnRowPointerLeft(MenuRow row)
    {
        if (row != _hoveredRow) return;

        row.RemoveHighlight();
        _hoveredRow = null;
        _hoverTimer.Stop();
    }

    private bool CursorMovedSinceOpening()
    {
        POINT now = MouseCursor.GetPosition();
        int distance = Math.Abs(now.X - _cursorWhenOpened.X) + Math.Abs(now.Y - _cursorWhenOpened.Y);
        return distance >= PixelsToMoveBeforeHoverCounts;
    }

    private void RestartHoverTimer()
    {
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private void OnHoverTimeReached()
    {
        if (_hoveredRow is not null) Choose(_hoveredRow.Entry);
    }

    private void Choose(MenuEntry entry)
    {
        Hide();
        EntryChosen?.Invoke(entry);
    }
}
