use windows::core::{w, PCWSTR};
use windows::Win32::Foundation::{BOOL, HWND, LPARAM, POINT, WPARAM};
use windows::Win32::Graphics::Gdi::{
    CreateBitmap, CreateDIBSection, DeleteObject, BITMAPINFO, BITMAPINFOHEADER, DIB_RGB_COLORS, HGDIOBJ,
};
use windows::Win32::UI::Shell::{
    Shell_NotifyIconW, NIF_ICON, NIF_INFO, NIF_MESSAGE, NIF_TIP, NIIF_INFO, NIM_ADD, NIM_DELETE, NIM_MODIFY,
    NOTIFYICONDATAW,
};
use windows::Win32::UI::WindowsAndMessaging::{
    AppendMenuW, CreateIconIndirect, CreatePopupMenu, DestroyMenu, GetCursorPos, GetSystemMetrics, PostMessageW,
    SetForegroundWindow, SetMenuDefaultItem, TrackPopupMenu, HICON, ICONINFO, MF_CHECKED, MF_SEPARATOR, MF_STRING,
    MF_UNCHECKED, SM_CXSMICON, TPM_BOTTOMALIGN, TPM_NONOTIFY, TPM_RETURNCMD, TPM_RIGHTBUTTON, WM_NULL,
};

use crate::util::fill_wide;
use crate::WM_APP_TRAY;

pub const ID_TOGGLE: u32 = 101;
pub const ID_SETTINGS: u32 = 102;
pub const ID_EXIT: u32 = 103;

fn tip(enabled: bool) -> &'static str {
    if enabled {
        "Shortcut Ring — включён. Удерживайте Win, чтобы выбрать из буфера"
    } else {
        "Shortcut Ring — выключен"
    }
}

fn data(hwnd: HWND) -> NOTIFYICONDATAW {
    NOTIFYICONDATAW {
        cbSize: std::mem::size_of::<NOTIFYICONDATAW>() as u32,
        hWnd: hwnd,
        uID: 1,
        uCallbackMessage: WM_APP_TRAY,
        ..Default::default()
    }
}

pub fn add(hwnd: HWND, icon: HICON, enabled: bool, welcome: bool) {
    let mut nid = data(hwnd);
    nid.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    nid.hIcon = icon;
    fill_wide(&mut nid.szTip, tip(enabled));
    if welcome {
        nid.uFlags |= NIF_INFO;
        nid.dwInfoFlags = NIIF_INFO;
        fill_wide(&mut nid.szInfoTitle, "ClipRing запущен");
        fill_wide(
            &mut nid.szInfo,
            "Удерживайте Win — вокруг курсора появится буфер обмена. Наведите на нужное и отпустите Win.",
        );
    }
    unsafe {
        let _ = Shell_NotifyIconW(NIM_ADD, &nid);
    }
}

pub fn update(hwnd: HWND, icon: HICON, enabled: bool) {
    let mut nid = data(hwnd);
    nid.uFlags = NIF_ICON | NIF_TIP;
    nid.hIcon = icon;
    fill_wide(&mut nid.szTip, tip(enabled));
    unsafe {
        let _ = Shell_NotifyIconW(NIM_MODIFY, &nid);
    }
}

pub fn remove(hwnd: HWND) {
    let nid = data(hwnd);
    unsafe {
        let _ = Shell_NotifyIconW(NIM_DELETE, &nid);
    }
}

/// Shows the tray menu and returns the chosen command id (0 = dismissed).
pub fn menu(hwnd: HWND, enabled: bool) -> u32 {
    unsafe {
        let Ok(m) = CreatePopupMenu() else { return 0 };
        let check = if enabled { MF_CHECKED } else { MF_UNCHECKED };
        let _ = AppendMenuW(m, MF_STRING | check, ID_TOGGLE as usize, w!("Включено"));
        let _ = AppendMenuW(m, MF_STRING, ID_SETTINGS as usize, w!("Настройки…"));
        let _ = AppendMenuW(m, MF_SEPARATOR, 0, PCWSTR::null());
        let _ = AppendMenuW(m, MF_STRING, ID_EXIT as usize, w!("Выход"));
        let _ = SetMenuDefaultItem(m, ID_SETTINGS, 0);
        let mut pt = POINT::default();
        let _ = GetCursorPos(&mut pt);
        // Without this the menu does not close when clicking elsewhere.
        let _ = SetForegroundWindow(hwnd);
        let cmd = TrackPopupMenu(
            m,
            TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN,
            pt.x,
            pt.y,
            0,
            hwnd,
            None,
        );
        let _ = PostMessageW(hwnd, WM_NULL, WPARAM(0), LPARAM(0));
        let _ = DestroyMenu(m);
        cmd.0 as u32
    }
}

/// Draws a small clipboard: teal when on, grey when off. No resource file needed.
pub fn make_icon(on: bool) -> HICON {
    let size = unsafe { GetSystemMetrics(SM_CXSMICON) }.max(16);
    let (body, clip) = if on { (0xFF0F6E73u32, 0xFF1A2230u32) } else { (0xFF8A8A8Au32, 0xFF505050u32) };
    let white = 0xFFFFFFFFu32;

    let in_rounded = |u: f64, v: f64, l: f64, t: f64, r: f64, b: f64, rad: f64| {
        if u < l || u > r || v < t || v > b {
            return false;
        }
        let cx = u.clamp(l + rad, r - rad);
        let cy = v.clamp(t + rad, b - rad);
        (u - cx).hypot(v - cy) <= rad
    };
    let pixel = |x: i32, y: i32| -> u32 {
        let (u, v) = ((x as f64 + 0.5) / size as f64, (y as f64 + 0.5) / size as f64);
        if (0.34..=0.66).contains(&u) && (0.04..=0.22).contains(&v) {
            return clip;
        }
        if !in_rounded(u, v, 0.12, 0.12, 0.88, 0.96, 0.14) {
            return 0;
        }
        if (0.24..=0.76).contains(&u) && (0.28..=0.86).contains(&v) {
            let line = (0.40..=0.47).contains(&v) || (0.55..=0.62).contains(&v) || ((0.70..=0.77).contains(&v) && u < 0.58);
            return if line && (0.32..=0.68).contains(&u) { body } else { white };
        }
        body
    };

    unsafe {
        let bmi = BITMAPINFO {
            bmiHeader: BITMAPINFOHEADER {
                biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
                biWidth: size,
                biHeight: -size, // top-down rows
                biPlanes: 1,
                biBitCount: 32,
                biCompression: 0, // BI_RGB
                ..Default::default()
            },
            ..Default::default()
        };
        let mut bits: *mut core::ffi::c_void = std::ptr::null_mut();
        let Ok(color) = CreateDIBSection(None, &bmi, DIB_RGB_COLORS, &mut bits, None, 0) else {
            return HICON::default();
        };
        let px = std::slice::from_raw_parts_mut(bits as *mut u32, (size * size) as usize);
        for y in 0..size {
            for x in 0..size {
                px[(y * size + x) as usize] = pixel(x, y);
            }
        }
        let mask = CreateBitmap(size, size, 1, 1, None);
        let info = ICONINFO { fIcon: BOOL(1), xHotspot: 0, yHotspot: 0, hbmMask: mask, hbmColor: color };
        let icon = CreateIconIndirect(&info).unwrap_or_default();
        let _ = DeleteObject(HGDIOBJ(color.0));
        let _ = DeleteObject(HGDIOBJ(mask.0));
        icon
    }
}
