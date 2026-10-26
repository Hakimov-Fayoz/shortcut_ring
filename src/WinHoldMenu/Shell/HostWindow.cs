using System.Diagnostics;
using System.Runtime.InteropServices;
using WinHoldMenu.Interop;

namespace WinHoldMenu.Shell;

internal sealed class HostWindow : IDisposable
{
    public const uint TrayIconMessage = Native.WM_APP + 1;

    private const string WindowClassName = "WinHoldMenu.HostWindow";
    private static readonly uint TaskbarCreatedMessage = Native.RegisterWindowMessageW("TaskbarCreated");

    private readonly Native.WndProc _messageHandler;

    public IntPtr Handle { get; }

    public event Action? ClipboardChanged;
    public event Action<uint>? TrayIconClicked;
    public event Action? TaskbarRestarted;

    public HostWindow()
    {
        _messageHandler = HandleMessage;
        IntPtr module = Native.GetModuleHandleW(null);

        RegisterWindowClass(module);
        Handle = Native.CreateWindowExW(0, WindowClassName, WindowClassName, 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, module, IntPtr.Zero);
        if (Handle == IntPtr.Zero) throw new InvalidOperationException("Could not create the host window.");

        Native.AddClipboardFormatListener(Handle);
    }

    private void RegisterWindowClass(IntPtr module)
    {
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_messageHandler),
            hInstance = module,
            lpszClassName = WindowClassName,
        };
        Native.RegisterClassExW(ref windowClass);
    }

    private IntPtr HandleMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (message == Native.WM_CLIPBOARDUPDATE)
                ClipboardChanged?.Invoke();
            else if (message == TrayIconMessage)
                TrayIconClicked?.Invoke(LowWord(lParam));
            else if (message == TaskbarCreatedMessage)
                TaskbarRestarted?.Invoke();
            else
                return Native.DefWindowProcW(window, message, wParam, lParam);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
        return IntPtr.Zero;
    }

    private static uint LowWord(IntPtr value) => (uint)(value.ToInt64() & 0xFFFF);

    public void Dispose()
    {
        Native.RemoveClipboardFormatListener(Handle);
        Native.DestroyWindow(Handle);
    }
}
