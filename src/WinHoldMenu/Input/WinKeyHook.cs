using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using WinHoldMenu.Interop;

namespace WinHoldMenu.Input;

internal sealed class WinKeyHook : IDisposable
{
    private enum State { Idle, WaitingForHold, MenuOpen, Finished }

    private const int KeyIsDownBit = 0x8000;
    private static readonly IntPtr BlockKey = new(1);

    private readonly Native.LowLevelProc _hookCallback;
    private readonly DispatcherQueueTimer _holdTimer;
    private IntPtr _hookHandle;

    private State _state = State.Idle;
    private ushort _heldWinKey;
    private bool _winAlreadyReleasedByUs;

    public event Action? MenuRequested;
    public event Action? MenuCancelled;
    public event Action? WinReleasedWhileMenuOpen;

    public bool Enabled { get; set; } = true;

    public WinKeyHook(int holdMs)
    {
        _hookCallback = OnKeyboardEvent;

        _holdTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _holdTimer.IsRepeating = false;
        _holdTimer.Tick += (_, _) => OnHoldTimeReached();
        SetHoldTime(holdMs);

        _hookHandle = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _hookCallback, Native.GetModuleHandleW(null), 0);
    }

    public void SetHoldTime(int ms) => _holdTimer.Interval = TimeSpan.FromMilliseconds(ms);

    public void ReleaseWinBeforeAction()
    {
        if (_state != State.MenuOpen) return;

        _state = State.Finished;
        KeyboardInput.ReleaseKey(_heldWinKey);
        _winAlreadyReleasedByUs = true;
    }

    private void OnHoldTimeReached()
    {
        if (_state != State.WaitingForHold) return;

        _state = State.MenuOpen;
        KeyboardInput.PreventStartMenuOnWinRelease();
        MenuRequested?.Invoke();
    }

    private IntPtr OnKeyboardEvent(int code, IntPtr message, IntPtr data)
    {
        if (code == Native.HC_ACTION && ShouldBlock((uint)message, data))
            return BlockKey;

        return Native.CallNextHookEx(_hookHandle, code, message, data);
    }

    private bool ShouldBlock(uint message, IntPtr data)
    {
        try
        {
            var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(data);
            if (key.dwExtraInfo == KeyboardInput.OurInputMarker) return false;

            bool isKeyDown = message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN;
            bool isKeyUp = message == Native.WM_KEYUP || message == Native.WM_SYSKEYUP;

            if (!KeyboardInput.IsWinKey(key.vkCode))
            {
                if (isKeyDown) OnOtherKeyDown();
                return false;
            }

            if (isKeyDown) return OnWinDown((ushort)key.vkCode);
            if (isKeyUp) return OnWinUp((ushort)key.vkCode);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool OnWinDown(ushort winKey)
    {
        bool isAutoRepeat = _state != State.Idle;
        if (isAutoRepeat) return _winAlreadyReleasedByUs;

        _heldWinKey = winKey;

        if (!Enabled || IsCtrlShiftOrAltDown())
        {
            _state = State.Finished;
            return false;
        }

        _state = State.WaitingForHold;
        _holdTimer.Stop();
        _holdTimer.Start();
        return false;
    }

    private bool OnWinUp(ushort winKey)
    {
        if (_state == State.Idle || winKey != _heldWinKey) return false;

        State stateBeforeRelease = _state;
        bool block = _winAlreadyReleasedByUs;

        _state = State.Idle;
        _winAlreadyReleasedByUs = false;

        if (stateBeforeRelease == State.WaitingForHold) _holdTimer.Stop();
        if (stateBeforeRelease == State.MenuOpen) WinReleasedWhileMenuOpen?.Invoke();

        return block;
    }

    private void OnOtherKeyDown()
    {
        switch (_state)
        {
            case State.WaitingForHold:
                _holdTimer.Stop();
                _state = State.Finished;
                break;

            case State.MenuOpen:
                _state = State.Finished;
                MenuCancelled?.Invoke();
                break;
        }
    }

    private static bool IsCtrlShiftOrAltDown() =>
        IsKeyDown(Native.VK_CONTROL) || IsKeyDown(Native.VK_SHIFT) || IsKeyDown(Native.VK_MENU);

    private static bool IsKeyDown(int key) => (Native.GetAsyncKeyState(key) & KeyIsDownBit) != 0;

    public void Dispose()
    {
        _holdTimer.Stop();
        if (_hookHandle == IntPtr.Zero) return;

        Native.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }
}
