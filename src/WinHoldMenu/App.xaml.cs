using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinHoldMenu.Clipboard;
using WinHoldMenu.Configuration;
using WinHoldMenu.Input;
using WinHoldMenu.Interop;
using WinHoldMenu.Shell;
using WinHoldMenu.UI;

namespace WinHoldMenu;

public partial class App : Application
{
    private const int HistoryCapacity = 10;
    private const int DelayBeforePasteMs = 40;
    private const string SettingsEditor = "notepad.exe";
    private const string TooltipWhenEnabled = "WinHoldMenu — удерживайте Win";
    private const string TooltipWhenDisabled = "WinHoldMenu — выключено";

    private readonly ClipboardHistory _history = new(HistoryCapacity);
    private Settings _settings = null!;
    private DispatcherQueue _uiQueue = null!;
    private HostWindow _hostWindow = null!;
    private TrayIcon _trayIcon = null!;
    private HoldMenuWindow _menu = null!;
    private WinKeyHook _winKeyHook = null!;
    private uint _clipboardChangeMadeByUs;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _settings = Settings.Load();
        _uiQueue = DispatcherQueue.GetForCurrentThread();

        CreateHostWindowAndTrayIcon();
        CreateMenu();
        CreateWinKeyHook();

        RememberCurrentClipboard();
        Autostart.UpdatePathIfEnabled();
    }

    private void CreateHostWindowAndTrayIcon()
    {
        _hostWindow = new HostWindow();
        _trayIcon = new TrayIcon(_hostWindow.Handle, HostWindow.TrayIconMessage, TooltipWhenEnabled);
        _trayIcon.Show();

        _hostWindow.ClipboardChanged += OnClipboardChanged;
        _hostWindow.TrayIconClicked += OnTrayIconClicked;
        _hostWindow.TaskbarRestarted += _trayIcon.Show;
    }

    private void CreateMenu()
    {
        _menu = new HoldMenuWindow();
        _menu.EntryChosen += Run;
    }

    private void CreateWinKeyHook()
    {
        _winKeyHook = new WinKeyHook(_settings.HoldMs);
        _winKeyHook.MenuRequested += ShowMenu;
        _winKeyHook.MenuCancelled += () => _uiQueue.TryEnqueue(_menu.Hide);
        _winKeyHook.WinReleasedWhileMenuOpen += () => _uiQueue.TryEnqueue(OnWinReleased);
    }

    private void ShowMenu()
    {
        _settings = Settings.Load();
        _winKeyHook.SetHoldTime(_settings.HoldMs);
        _menu.Show(CollectTextEntries(), CollectWindowEntries(), _settings.TriggerOnHover, _settings.HoverDwellMs);
    }

    private List<TextEntry> CollectTextEntries()
    {
        var pinned = _settings.PinnedTexts
            .Select(text => new TextEntry(text, IsPinned: true));

        var recent = _history.Texts
            .Where(text => !_settings.PinnedTexts.Contains(text))
            .Take(_settings.HistorySize)
            .Select(text => new TextEntry(text, IsPinned: false));

        return pinned.Concat(recent).ToList();
    }

    private List<WindowEntry> CollectWindowEntries() =>
        OpenWindowFinder.FindMostRecent(_settings.MaxWindows)
            .Select(window => new WindowEntry(window))
            .ToList();

    private void OnWinReleased()
    {
        if (!_menu.IsOpen) return;

        MenuEntry? entry = _settings.TriggerOnHover ? null : _menu.HoveredEntry;
        _menu.Hide();

        if (entry is not null) Run(entry);
    }

    private void Run(MenuEntry entry)
    {
        _winKeyHook.ReleaseWinBeforeAction();

        switch (entry)
        {
            case TextEntry text:
                PasteText(text.Text);
                break;

            case WindowEntry window:
                WindowActivator.BringToFront(window.Window.Handle);
                break;
        }
    }

    private async void PasteText(string text)
    {
        if (!ClipboardText.Write(_hostWindow.Handle, text)) return;
        _clipboardChangeMadeByUs = ClipboardText.ChangeNumber;

        await Task.Delay(DelayBeforePasteMs);
        KeyboardInput.PressCtrlV();
    }

    private void OnClipboardChanged()
    {
        bool changedByUs = ClipboardText.ChangeNumber == _clipboardChangeMadeByUs;
        if (!changedByUs) RememberCurrentClipboard();
    }

    private void RememberCurrentClipboard()
    {
        string? text = ClipboardText.Read(_hostWindow.Handle);
        if (text is not null) _history.Add(text);
    }

    private void OnTrayIconClicked(uint mouseMessage)
    {
        if (mouseMessage == Native.WM_LBUTTONDBLCLK) OpenSettingsFile();
        if (mouseMessage == Native.WM_RBUTTONUP) ShowTrayMenu();
    }

    private void ShowTrayMenu()
    {
        TrayCommand command = _trayIcon.ShowMenu(
            new TrayMenuItem(TrayCommand.ToggleEnabled, "Включено", _winKeyHook.Enabled),
            new TrayMenuItem(TrayCommand.ToggleAutostart, "Запускать вместе с Windows", Autostart.IsEnabled),
            new TrayMenuItem(TrayCommand.OpenSettings, "Настройки…"),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayCommand.Exit, "Выход"));

        switch (command)
        {
            case TrayCommand.ToggleEnabled:
                ToggleEnabled();
                break;

            case TrayCommand.ToggleAutostart:
                Autostart.Set(!Autostart.IsEnabled);
                break;

            case TrayCommand.OpenSettings:
                OpenSettingsFile();
                break;

            case TrayCommand.Exit:
                Quit();
                break;
        }
    }

    private void ToggleEnabled()
    {
        _winKeyHook.Enabled = !_winKeyHook.Enabled;
        if (!_winKeyHook.Enabled) _menu.Hide();

        _trayIcon.SetTooltip(_winKeyHook.Enabled ? TooltipWhenEnabled : TooltipWhenDisabled);
    }

    private void OpenSettingsFile()
    {
        if (!File.Exists(Settings.FilePath)) _settings.TrySave();
        Process.Start(SettingsEditor, $"\"{Settings.FilePath}\"");
    }

    private void Quit()
    {
        _menu.Hide();
        _winKeyHook.Dispose();
        _trayIcon.Dispose();
        _hostWindow.Dispose();
        Exit();
    }
}
