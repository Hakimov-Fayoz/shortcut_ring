using Microsoft.UI.Dispatching;
using WinHoldMenu.Interop;

namespace WinHoldMenu.Input;

/// <summary>
/// Low-level keyboard hook that detects "Win held for N ms" without breaking
/// normal Win shortcuts or a quick tap (Start menu).
/// </summary>
internal sealed unsafe class WinKeyHook : IDisposable
{
    private enum St { Idle, Pending, Menu, Done }

    private St _st = St.Idle;
    private uint _winVk;
    private bool _injectedUp;   // we already released Win virtually -> swallow the physical up/repeats

    private readonly Native.LowLevelProc _proc;   // keep delegate alive
    private readonly DispatcherQueueTimer _timer;
    private IntPtr _hook;

    /// <summary>Win held long enough: show the menu.</summary>
    public event Action? HoldReached;
    /// <summary>Another key was pressed while the menu was open: hide it.</summary>
    public event Action? Dismissed;
    /// <summary>Win physically released while the menu was open (raised inside the hook!).</summary>
    public event Action? WinReleased;

    public WinKeyHook(int holdMs)
    {
        _proc = Proc;
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(holdMs);
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => OnHold();
        _hook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandleW(null), 0);
    }

    private void OnHold()
    {
        if (_st != St.Pending) return;
        _st = St.Menu;
        Actions.InjectMask();
        HoldReached?.Invoke();
    }

    /// <summary>
    /// Call right before pasting / activating a window while Win is still physically held:
    /// releases Win virtually so the target app does not see Win+Ctrl+V and similar.
    /// </summary>
    public void BeginAction()
    {
        if (_st != St.Menu) return;
        _st = St.Done;
        Actions.SendWinUp(_winVk);
        _injectedUp = true;
    }

    private static bool AnyModifierDown() =>
        (Native.GetAsyncKeyState(Native.VK_CONTROL) & 0x8000) != 0 ||
        (Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) != 0 ||
        (Native.GetAsyncKeyState(Native.VK_MENU) & 0x8000) != 0;

    private IntPtr Proc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == Native.HC_ACTION)
        {
            try
            {
                var k = (KBDLLHOOKSTRUCT*)lParam;
                if (k->dwExtraInfo != Actions.Marker)
                {
                    uint m = (uint)wParam.ToInt64();
                    bool down = m == Native.WM_KEYDOWN || m == Native.WM_SYSKEYDOWN;
                    bool up = m == Native.WM_KEYUP || m == Native.WM_SYSKEYUP;
                    uint vk = k->vkCode;

                    if (vk == Native.VK_LWIN || vk == Native.VK_RWIN)
                    {
                        if (down && OnWinDown(vk)) return (IntPtr)1;
                        if (up && OnWinUp(vk)) return (IntPtr)1;
                    }
                    else if (down && (_st == St.Pending || _st == St.Menu))
                    {
                        // Win+<key> shortcut: not our gesture
                        if (_st == St.Pending) _timer.Stop();
                        else Dismissed?.Invoke();
                        _st = St.Done;
                    }
                }
            }
            catch
            {
                // never let an exception escape the hook
            }
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <returns>true = swallow the event</returns>
    private bool OnWinDown(uint vk)
    {
        if (_st == St.Idle)
        {
            _winVk = vk;
            if (AnyModifierDown())          // Ctrl/Shift/Alt+Win combos: leave alone
            {
                _st = St.Done;
                return false;
            }
            _st = St.Pending;
            _timer.Stop();
            _timer.Start();
            return false;
        }
        return _injectedUp;                 // auto-repeat after our virtual release
    }

    /// <returns>true = swallow the event</returns>
    private bool OnWinUp(uint vk)
    {
        if (_st == St.Idle || vk != _winVk) return false;

        bool swallow = _injectedUp;
        St prev = _st;
        _st = St.Idle;
        _injectedUp = false;

        if (prev == St.Pending) _timer.Stop();
        else if (prev == St.Menu) WinReleased?.Invoke();
        return swallow;
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
