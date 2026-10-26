//! Low-level keyboard hook: notices Win being held alone, and keeps Start closed
//! when Win is released after the ring was shown.
//!
//! The hook must answer fast, so it only flips flags and posts messages to the main
//! window; all real work happens there.

use std::cell::Cell;

use windows::Win32::Foundation::{HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::Input::KeyboardAndMouse::{
    SendInput, INPUT, INPUT_0, INPUT_KEYBOARD, KEYBDINPUT, KEYBD_EVENT_FLAGS, KEYEVENTF_EXTENDEDKEY,
    KEYEVENTF_KEYUP, VIRTUAL_KEY, VK_CONTROL, VK_ESCAPE, VK_LWIN, VK_RWIN,
};
use windows::Win32::UI::WindowsAndMessaging::{
    CallNextHookEx, PostMessageW, SetWindowsHookExW, UnhookWindowsHookEx, HC_ACTION, HHOOK, KBDLLHOOKSTRUCT,
    LLKHF_INJECTED, WH_KEYBOARD_LL, WM_KEYDOWN, WM_KEYUP, WM_SYSKEYDOWN, WM_SYSKEYUP,
};

use crate::{WM_APP_CANCEL, WM_APP_COMMIT, WM_APP_ESC, WM_APP_WIN_DOWN};

/// Unassigned virtual key: tapping it between Win down and Win up makes Windows treat
/// the press as a combination, so the Start menu stays closed (same trick as AutoHotkey).
const VK_MASK: u16 = 0xE8;
const VK_V: u16 = 0x56;
/// Tag on our own SendInput events so the hook lets them through untouched.
const OWN_INPUT: usize = 0xC11B_0001;

#[derive(Default)]
struct State {
    main: Cell<HWND>,
    hook: Cell<HHOOK>,
    enabled: Cell<bool>,
    win_down: Cell<bool>,
    /// Another key was pressed while Win was down: it is a shortcut like Win+E, not ours.
    combo: Cell<bool>,
    /// The ring is (or was, until Win goes up) on screen.
    popup: Cell<bool>,
}

thread_local! {
    static ST: State = State::default();
}

pub fn install(main: HWND, enabled: bool) -> windows::core::Result<()> {
    unsafe {
        let module = GetModuleHandleW(None)?;
        let hook = SetWindowsHookExW(WH_KEYBOARD_LL, Some(proc), module, 0)?;
        ST.with(|s| {
            s.main.set(main);
            s.hook.set(hook);
            s.enabled.set(enabled);
        });
    }
    Ok(())
}

pub fn uninstall() {
    ST.with(|s| unsafe {
        let _ = UnhookWindowsHookEx(s.hook.get());
    });
}

pub fn set_enabled(on: bool) {
    ST.with(|s| s.enabled.set(on));
}

/// Win is still held alone after the delay — time to show the ring.
pub fn should_show() -> bool {
    ST.with(|s| s.enabled.get() && s.win_down.get() && !s.combo.get())
}

pub fn set_popup(on: bool) {
    ST.with(|s| s.popup.set(on));
}

fn post(msg: u32) {
    ST.with(|s| unsafe {
        let _ = PostMessageW(s.main.get(), msg, WPARAM(0), LPARAM(0));
    });
}

unsafe extern "system" fn proc(code: i32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    if code == HC_ACTION as i32 {
        let k = &*(lparam.0 as *const KBDLLHOOKSTRUCT);
        let own = k.flags.0 & LLKHF_INJECTED.0 != 0 && k.dwExtraInfo == OWN_INPUT;
        if !own && handle(k.vkCode, wparam.0 as u32) {
            return LRESULT(1);
        }
    }
    CallNextHookEx(None, code, wparam, lparam)
}

/// Returns true to swallow the key event.
fn handle(vk: u32, msg: u32) -> bool {
    let down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
    let up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
    let is_win = vk == VK_LWIN.0 as u32 || vk == VK_RWIN.0 as u32;

    ST.with(|s| {
        if is_win && down {
            // Auto-repeat sends more key-downs while held; react only to the first one.
            if !s.win_down.get() {
                s.win_down.set(true);
                s.combo.set(false);
                s.popup.set(false);
                if s.enabled.get() {
                    post(WM_APP_WIN_DOWN);
                }
            }
            return false;
        }

        if is_win && up {
            if !s.win_down.get() {
                return false;
            }
            s.win_down.set(false);
            if s.popup.get() {
                s.popup.set(false);
                // Swallow the real Win-up and replay it after the mask key,
                // so the mask is guaranteed to arrive first.
                send(&[key(VK_MASK, false), key(VK_MASK, true), key(vk as u16, true)]);
                post(WM_APP_COMMIT);
                return true;
            }
            post(WM_APP_CANCEL);
            return false;
        }

        if down && s.win_down.get() {
            if s.popup.get() && vk == VK_ESCAPE.0 as u32 {
                // Esc closes the ring; Win-up stays masked so Start does not pop up.
                post(WM_APP_ESC);
                return true;
            }
            s.combo.set(true);
            if s.popup.get() {
                s.popup.set(false);
                post(WM_APP_CANCEL);
            }
        }
        false
    })
}

fn key(vk: u16, up: bool) -> INPUT {
    let mut flags = KEYBD_EVENT_FLAGS(0);
    if up {
        flags |= KEYEVENTF_KEYUP;
    }
    if vk == VK_LWIN.0 || vk == VK_RWIN.0 {
        flags |= KEYEVENTF_EXTENDEDKEY;
    }
    INPUT {
        r#type: INPUT_KEYBOARD,
        Anonymous: INPUT_0 {
            ki: KEYBDINPUT { wVk: VIRTUAL_KEY(vk), wScan: 0, dwFlags: flags, time: 0, dwExtraInfo: OWN_INPUT },
        },
    }
}

fn send(inputs: &[INPUT]) {
    unsafe {
        SendInput(inputs, std::mem::size_of::<INPUT>() as i32);
    }
}

pub fn send_ctrl_v() {
    let ctrl = VK_CONTROL.0;
    send(&[key(ctrl, false), key(VK_V, false), key(VK_V, true), key(ctrl, true)]);
}
