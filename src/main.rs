//! ClipRing — hold Win to see the clipboard around the cursor; point at an item and
//! release Win to paste it. Tray icon: on/off, settings (own words), exit.

#![windows_subsystem = "windows"]

mod app;
mod autostart;
mod clipboard;
mod config;
mod hook;
mod popup;
mod settings;
mod tray;
mod util;

use windows::core::{w, PCWSTR};
use windows::Win32::Foundation::{GetLastError, ERROR_ALREADY_EXISTS, HINSTANCE, HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::Graphics::Gdi::{GetSysColorBrush, COLOR_BTNFACE, HBRUSH};
use windows::Win32::System::ApplicationInstallationAndServicing::{CreateActCtxW, ACTCTXW};
use windows::Win32::System::DataExchange::{AddClipboardFormatListener, GetClipboardSequenceNumber};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::System::Threading::CreateMutexW;
use windows::Win32::UI::HiDpi::{SetProcessDpiAwarenessContext, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2};
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DestroyWindow, DispatchMessageW, GetMessageW, IsDialogMessageW, KillTimer,
    LoadCursorW, MessageBoxW, PostQuitMessage, RegisterClassExW, RegisterWindowMessageW, SetTimer,
    TranslateMessage, HICON, IDC_ARROW, MB_ICONINFORMATION, MB_ICONERROR, MB_OK, MSG, WINDOW_EX_STYLE, WM_APP,
    WM_CONTEXTMENU, WM_DESTROY, WM_ERASEBKGND, WM_LBUTTONDBLCLK, WM_NCHITTEST, WM_PAINT, WM_RBUTTONUP, WM_TIMER,
    WNDCLASSEXW, WNDPROC, WS_EX_LAYERED, WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW, WS_EX_TOPMOST, WS_EX_TRANSPARENT,
    WS_OVERLAPPEDWINDOW, WS_POPUP,
};

use config::Config;

pub const WM_APP_WIN_DOWN: u32 = WM_APP + 1;
pub const WM_APP_COMMIT: u32 = WM_APP + 2;
pub const WM_APP_CANCEL: u32 = WM_APP + 3;
pub const WM_APP_ESC: u32 = WM_APP + 4;
pub const WM_APP_TRAY: u32 = WM_APP + 5;

pub const TIMER_HOLD: usize = 1;
pub const TIMER_TRACK: usize = 2;
pub const TIMER_PASTE: usize = 3;

const WM_CLIPBOARDUPDATE: u32 = 0x031D;
const HTTRANSPARENT: isize = -1;
/// Time between Win going up and Ctrl+V, so the target app sees Win already released.
const PASTE_DELAY_MS: u32 = 40;

fn main() {
    unsafe {
        let _ = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        enable_visual_styles();

        let _mutex = CreateMutexW(None, true, w!("Local\\ClipRing.SingleInstance"));
        if GetLastError() == ERROR_ALREADY_EXISTS {
            MessageBoxW(None, w!("ClipRing уже запущен — его значок в трее."), w!("ClipRing"), MB_OK | MB_ICONINFORMATION);
            return;
        }

        if let Err(e) = run() {
            let msg = util::wide(&format!("ClipRing не смог запуститься:\n{e}"));
            MessageBoxW(None, PCWSTR(msg.as_ptr()), w!("ClipRing"), MB_OK | MB_ICONERROR);
        }
    }
}

unsafe fn run() -> windows::core::Result<()> {
    let hinst: HINSTANCE = GetModuleHandleW(None)?.into();
    let (cfg, existed) = Config::load();

    register(hinst, w!("ClipRingMain"), Some(main_proc), HBRUSH::default())?;
    register(hinst, w!("ClipRingPopup"), Some(popup_proc), HBRUSH::default())?;
    register(hinst, settings::CLASS, Some(settings::proc), GetSysColorBrush(COLOR_BTNFACE))?;

    let main = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("ClipRingMain"),
        w!("ClipRing"),
        WS_OVERLAPPEDWINDOW,
        0,
        0,
        0,
        0,
        None,
        None,
        hinst,
        None,
    )?;
    let popup = CreateWindowExW(
        WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT,
        w!("ClipRingPopup"),
        w!(""),
        WS_POPUP,
        0,
        0,
        1,
        1,
        None,
        None,
        hinst,
        None,
    )?;
    popup::init_window(popup);

    let (icon_on, icon_off) = (tray::make_icon(true), tray::make_icon(false));
    let enabled = cfg.enabled;
    app::with(|a| {
        a.cfg = cfg;
        a.hinst = hinst;
        a.main = main;
        a.popup = popup;
        a.icon_on = icon_on;
        a.icon_off = icon_off;
    });
    if !existed {
        let _ = app::with(|a| a.cfg.save());
    }

    hook::install(main, enabled)?;
    tray::add(main, icon(enabled), enabled, !existed);
    AddClipboardFormatListener(main)?;
    if let Some(text) = clipboard::read_text(main) {
        app::with(|a| a.remember(text));
    }
    // Keep the autostart entry pointing at this exe if it was moved.
    if autostart::is_enabled() {
        autostart::set(true);
    }

    let mut msg = MSG::default();
    while GetMessageW(&mut msg, None, 0, 0).0 > 0 {
        let settings = app::with(|a| a.settings);
        if !settings.0.is_null() && IsDialogMessageW(settings, &msg).as_bool() {
            continue;
        }
        let _ = TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    hook::uninstall();
    Ok(())
}

unsafe fn register(hinst: HINSTANCE, name: PCWSTR, proc: WNDPROC, brush: HBRUSH) -> windows::core::Result<()> {
    let wc = WNDCLASSEXW {
        cbSize: std::mem::size_of::<WNDCLASSEXW>() as u32,
        lpfnWndProc: proc,
        hInstance: hinst,
        hCursor: LoadCursorW(None, IDC_ARROW)?,
        hbrBackground: brush,
        lpszClassName: name,
        ..Default::default()
    };
    if RegisterClassExW(&wc) == 0 {
        return Err(windows::core::Error::from_win32());
    }
    Ok(())
}

/// Modern-looking buttons and edits without embedding a manifest: borrow the
/// common-controls v6 manifest that ships inside shell32.dll.
unsafe fn enable_visual_styles() {
    const ACTCTX_FLAG_RESOURCE_NAME_VALID: u32 = 0x8;
    const ACTCTX_FLAG_SET_PROCESS_DEFAULT: u32 = 0x10;
    let root = std::env::var("SystemRoot").unwrap_or_else(|_| "C:\\Windows".into());
    let source = util::wide(&format!("{root}\\System32\\shell32.dll"));
    let ctx = ACTCTXW {
        cbSize: std::mem::size_of::<ACTCTXW>() as u32,
        dwFlags: ACTCTX_FLAG_RESOURCE_NAME_VALID | ACTCTX_FLAG_SET_PROCESS_DEFAULT,
        lpSource: PCWSTR(source.as_ptr()),
        lpResourceName: PCWSTR(124 as *const u16),
        ..Default::default()
    };
    let _ = CreateActCtxW(&ctx);
}

fn icon(enabled: bool) -> HICON {
    app::with(|a| if enabled { a.icon_on } else { a.icon_off })
}

fn set_enabled(on: bool) {
    let (main, cfg) = app::with(|a| {
        a.cfg.enabled = on;
        (a.main, a.cfg.clone())
    });
    let _ = cfg.save();
    hook::set_enabled(on);
    if !on {
        popup::hide();
    }
    tray::update(main, icon(on), on);
}

fn on_clipboard(hwnd: HWND) {
    let seq = unsafe { GetClipboardSequenceNumber() };
    if seq == app::with(|a| a.own_seq) {
        return;
    }
    if let Some(text) = clipboard::read_text(hwnd) {
        app::with(|a| a.remember(text));
    }
}

fn paste(hwnd: HWND) {
    let Some(item) = app::with(|a| a.pending.take()) else { return };
    if !clipboard::write_text(hwnd, &item.text) {
        return;
    }
    // Not moved to the front: items keep their places in the ring, so the hand learns them.
    app::with(|a| a.own_seq = unsafe { GetClipboardSequenceNumber() });
    hook::send_ctrl_v();
}

thread_local! {
    static TASKBAR_CREATED: u32 = unsafe { RegisterWindowMessageW(w!("TaskbarCreated")) };
}

unsafe extern "system" fn main_proc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    match msg {
        WM_CLIPBOARDUPDATE => on_clipboard(hwnd),
        WM_APP_WIN_DOWN => {
            let ms = app::with(|a| a.cfg.hold_ms);
            SetTimer(hwnd, TIMER_HOLD, ms, None);
        }
        WM_APP_CANCEL => {
            let _ = KillTimer(hwnd, TIMER_HOLD);
            popup::hide();
        }
        WM_APP_ESC => popup::hide(),
        WM_APP_COMMIT => {
            let _ = KillTimer(hwnd, TIMER_HOLD);
            let chosen = popup::selection();
            popup::hide();
            if chosen.is_some() {
                app::with(|a| a.pending = chosen);
                SetTimer(hwnd, TIMER_PASTE, PASTE_DELAY_MS, None);
            }
        }
        WM_TIMER => match wparam.0 {
            TIMER_HOLD => {
                let _ = KillTimer(hwnd, TIMER_HOLD);
                if hook::should_show() {
                    popup::show();
                    hook::set_popup(true);
                }
            }
            TIMER_TRACK => popup::track(),
            TIMER_PASTE => {
                let _ = KillTimer(hwnd, TIMER_PASTE);
                paste(hwnd);
            }
            _ => {}
        },
        WM_APP_TRAY => match (lparam.0 & 0xFFFF) as u32 {
            WM_RBUTTONUP | WM_CONTEXTMENU => {
                let enabled = app::with(|a| a.cfg.enabled);
                match tray::menu(hwnd, enabled) {
                    tray::ID_TOGGLE => set_enabled(!enabled),
                    tray::ID_SETTINGS => settings::open(),
                    tray::ID_EXIT => {
                        let _ = DestroyWindow(hwnd);
                    }
                    _ => {}
                }
            }
            WM_LBUTTONDBLCLK => settings::open(),
            _ => {}
        },
        WM_DESTROY => {
            tray::remove(hwnd);
            PostQuitMessage(0);
        }
        m if m == TASKBAR_CREATED.with(|t| *t) => {
            // Explorer restarted: the tray icon has to be added again.
            let enabled = app::with(|a| a.cfg.enabled);
            tray::add(hwnd, icon(enabled), enabled, false);
        }
        _ => return DefWindowProcW(hwnd, msg, wparam, lparam),
    }
    LRESULT(0)
}

unsafe extern "system" fn popup_proc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    match msg {
        WM_PAINT => {
            popup::paint(hwnd);
            LRESULT(0)
        }
        WM_ERASEBKGND => LRESULT(1),
        WM_NCHITTEST => LRESULT(HTTRANSPARENT),
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}
