//! Settings window: own words (one per line), hold delay, ring size, autostart.

use windows::core::{w, PCWSTR};
use windows::Win32::Foundation::{HWND, LPARAM, LRESULT, RECT, WPARAM};
use windows::Win32::Graphics::Gdi::{CreateFontIndirectW, DeleteObject, CLEARTYPE_QUALITY, HFONT, HGDIOBJ, LOGFONTW};
use windows::Win32::UI::HiDpi::GetDpiForSystem;
use windows::Win32::UI::WindowsAndMessaging::{
    AdjustWindowRectEx, CreateWindowExW, DefWindowProcW, DestroyWindow, GetDlgItem, GetSystemMetrics,
    GetWindowTextLengthW, GetWindowTextW, MessageBoxW, SendMessageW, SetForegroundWindow, ShowWindow, HMENU,
    MB_ICONWARNING, MB_OK, SM_CXSCREEN, SM_CYSCREEN, SW_SHOW, WINDOW_EX_STYLE, WINDOW_STYLE, WM_CLOSE, WM_COMMAND,
    WM_DESTROY, WM_SETFONT, WM_SETICON, WS_CAPTION, WS_CHILD, WS_EX_CLIENTEDGE, WS_MINIMIZEBOX, WS_OVERLAPPED, WS_SYSMENU,
    WS_TABSTOP, WS_VISIBLE, WS_VSCROLL,
};

use crate::config::{Config, HOLD_MS_RANGE, MAX_ITEMS_RANGE};
use crate::util::{fill_wide, wide};
use crate::{app, autostart};

pub const CLASS: PCWSTR = w!("ClipRingSettings");

const ID_WORDS: i32 = 201;
const ID_HOLD: i32 = 202;
const ID_MAX: i32 = 203;
const ID_AUTOSTART: i32 = 204;
const IDOK: i32 = 1;
const IDCANCEL: i32 = 2;

// Classic control styles (plain numbers keep this independent of crate feature layout).
const ES_MULTILINE: u32 = 0x0004;
const ES_AUTOVSCROLL: u32 = 0x0040;
const ES_WANTRETURN: u32 = 0x1000;
const ES_NUMBER: u32 = 0x2000;
const ES_AUTOHSCROLL: u32 = 0x0080;
const BS_PUSHBUTTON: u32 = 0x0000;
const BS_DEFPUSHBUTTON: u32 = 0x0001;
const BS_AUTOCHECKBOX: u32 = 0x0003;
const BM_GETCHECK: u32 = 0x00F0;
const BM_SETCHECK: u32 = 0x00F1;
const BST_CHECKED: usize = 1;
const ICON_SMALL: u32 = 0;
const ICON_BIG: u32 = 1;

/// Opens the window, or brings it forward if it is already open.
pub fn open() {
    let (existing, hinst, cfg, icon) = app::with(|a| (a.settings, a.hinst, a.cfg.clone(), a.icon_on));
    unsafe {
        if !existing.0.is_null() {
            let _ = SetForegroundWindow(existing);
            return;
        }
        let s = GetDpiForSystem() as f64 / 96.0;
        let px = |v: i32| (v as f64 * s).round() as i32;

        let style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX;
        let mut rc = RECT { left: 0, top: 0, right: px(480), bottom: px(414) };
        let _ = AdjustWindowRectEx(&mut rc, style, false, WINDOW_EX_STYLE(0));
        let (w, h) = (rc.right - rc.left, rc.bottom - rc.top);
        let (x, y) = ((GetSystemMetrics(SM_CXSCREEN) - w) / 2, (GetSystemMetrics(SM_CYSCREEN) - h) / 2);
        let Ok(hwnd) = CreateWindowExW(
            WINDOW_EX_STYLE(0),
            CLASS,
            w!("ClipRing — настройки"),
            style,
            x,
            y,
            w,
            h,
            None,
            None,
            hinst,
            None,
        ) else {
            return;
        };

        for kind in [ICON_SMALL, ICON_BIG] {
            SendMessageW(hwnd, WM_SETICON, WPARAM(kind as usize), LPARAM(icon.0 as isize));
        }

        let mut lf = LOGFONTW { lfHeight: -px(15), lfQuality: CLEARTYPE_QUALITY, ..Default::default() };
        fill_wide(&mut lf.lfFaceName, "Segoe UI");
        let font = CreateFontIndirectW(&lf);

        let ctl = |class: PCWSTR, text: &str, style: u32, ex: WINDOW_EX_STYLE, r: (i32, i32, i32, i32), id: i32| {
            let text = wide(text);
            let child = CreateWindowExW(
                ex,
                class,
                PCWSTR(text.as_ptr()),
                WS_CHILD | WS_VISIBLE | WINDOW_STYLE(style),
                px(r.0),
                px(r.1),
                px(r.2),
                px(r.3),
                hwnd,
                HMENU(id as isize as *mut _),
                hinst,
                None,
            )
            .unwrap_or_default();
            SendMessageW(child, WM_SETFONT, WPARAM(font.0 as usize), LPARAM(1));
            child
        };
        let none = WINDOW_EX_STYLE(0);
        let words = cfg.words.join("\r\n");

        ctl(
            w!("STATIC"),
            "Свои слова и фразы — по одной на строке.\nОни видны в кольце всегда, даже если их нет в буфере.",
            0,
            none,
            (14, 10, 452, 40),
            -1,
        );
        ctl(
            w!("EDIT"),
            &words,
            WS_TABSTOP.0 | WS_VSCROLL.0 | ES_MULTILINE | ES_AUTOVSCROLL | ES_WANTRETURN,
            WS_EX_CLIENTEDGE,
            (14, 54, 452, 190),
            ID_WORDS,
        );
        ctl(w!("STATIC"), "Задержка до появления кольца, мс:", 0, none, (14, 262, 320, 24), -1);
        ctl(
            w!("EDIT"),
            &cfg.hold_ms.to_string(),
            WS_TABSTOP.0 | ES_NUMBER | ES_AUTOHSCROLL,
            WS_EX_CLIENTEDGE,
            (370, 258, 96, 26),
            ID_HOLD,
        );
        ctl(w!("STATIC"), "Сколько элементов показывать в кольце:", 0, none, (14, 298, 320, 24), -1);
        ctl(
            w!("EDIT"),
            &cfg.max_items.to_string(),
            WS_TABSTOP.0 | ES_NUMBER | ES_AUTOHSCROLL,
            WS_EX_CLIENTEDGE,
            (370, 294, 96, 26),
            ID_MAX,
        );
        let auto = ctl(
            w!("BUTTON"),
            "Запускать вместе с Windows",
            WS_TABSTOP.0 | BS_AUTOCHECKBOX,
            none,
            (14, 332, 320, 26),
            ID_AUTOSTART,
        );
        if autostart::is_enabled() {
            SendMessageW(auto, BM_SETCHECK, WPARAM(BST_CHECKED), LPARAM(0));
        }
        ctl(w!("BUTTON"), "Сохранить", WS_TABSTOP.0 | BS_DEFPUSHBUTTON, none, (250, 372, 104, 32), IDOK);
        ctl(w!("BUTTON"), "Отмена", WS_TABSTOP.0 | BS_PUSHBUTTON, none, (362, 372, 104, 32), IDCANCEL);

        app::with(|a| {
            a.settings = hwnd;
            a.settings_font = font;
        });
        let _ = ShowWindow(hwnd, SW_SHOW);
        let _ = SetForegroundWindow(hwnd);
    }
}

unsafe fn text_of(hwnd: HWND, id: i32) -> String {
    let Ok(ctl) = GetDlgItem(hwnd, id) else { return String::new() };
    let len = GetWindowTextLengthW(ctl).max(0) as usize;
    let mut buf = vec![0u16; len + 1];
    let n = GetWindowTextW(ctl, &mut buf).max(0) as usize;
    String::from_utf16_lossy(&buf[..n])
}

/// Returns false if saving failed and the window should stay open.
unsafe fn save(hwnd: HWND) -> bool {
    let mut words: Vec<String> = Vec::new();
    for line in text_of(hwnd, ID_WORDS).lines() {
        let line = line.trim();
        if !line.is_empty() && !words.iter().any(|w| w == line) {
            words.push(line.to_string());
        }
    }
    let base = app::with(|a| a.cfg.clone());
    let hold_ms = text_of(hwnd, ID_HOLD).trim().parse().unwrap_or(base.hold_ms);
    let max_items = text_of(hwnd, ID_MAX).trim().parse().unwrap_or(base.max_items);
    let cfg = Config { words, hold_ms, max_items, ..base }.clamped();

    let auto = GetDlgItem(hwnd, ID_AUTOSTART)
        .map(|c| SendMessageW(c, BM_GETCHECK, WPARAM(0), LPARAM(0)).0 as usize == BST_CHECKED)
        .unwrap_or(false);
    if auto != autostart::is_enabled() {
        autostart::set(auto);
    }

    if let Err(e) = cfg.save() {
        let msg = wide(&format!("Не удалось сохранить настройки в\n{}\n\n{e}", Config::path().display()));
        MessageBoxW(hwnd, PCWSTR(msg.as_ptr()), w!("ClipRing"), MB_OK | MB_ICONWARNING);
        return false;
    }
    if cfg.hold_ms != hold_ms || cfg.max_items != max_items {
        let msg = wide(&format!(
            "Значения поправлены до допустимых: задержка {}–{} мс, элементов {}–{}.",
            HOLD_MS_RANGE.0, HOLD_MS_RANGE.1, MAX_ITEMS_RANGE.0, MAX_ITEMS_RANGE.1
        ));
        MessageBoxW(hwnd, PCWSTR(msg.as_ptr()), w!("ClipRing"), MB_OK | MB_ICONWARNING);
    }
    app::with(|a| a.cfg = cfg);
    true
}

pub unsafe extern "system" fn proc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    match msg {
        WM_COMMAND => {
            match (wparam.0 & 0xFFFF) as i32 {
                IDOK => {
                    if save(hwnd) {
                        let _ = DestroyWindow(hwnd);
                    }
                }
                IDCANCEL => {
                    let _ = DestroyWindow(hwnd);
                }
                _ => {}
            }
            LRESULT(0)
        }
        WM_CLOSE => {
            let _ = DestroyWindow(hwnd);
            LRESULT(0)
        }
        WM_DESTROY => {
            let font: HFONT = app::with(|a| {
                a.settings = HWND::default();
                std::mem::take(&mut a.settings_font)
            });
            let _ = DeleteObject(HGDIOBJ(font.0));
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}
