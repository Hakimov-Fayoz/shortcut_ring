use windows::core::w;
use windows::Win32::Foundation::{GlobalFree, HANDLE, HGLOBAL, HWND};
use windows::Win32::System::DataExchange::{
    CloseClipboard, EmptyClipboard, GetClipboardData, IsClipboardFormatAvailable, OpenClipboard,
    RegisterClipboardFormatW, SetClipboardData,
};
use windows::Win32::System::Memory::{GlobalAlloc, GlobalLock, GlobalSize, GlobalUnlock, GMEM_MOVEABLE};
use windows::Win32::System::Threading::Sleep;

const CF_UNICODETEXT: u32 = 13;
/// Longer texts are not kept in the history.
const MAX_TEXT_CHARS: usize = 100_000;

/// Another program may hold the clipboard for a moment — retry briefly.
unsafe fn open(owner: HWND) -> bool {
    for _ in 0..10 {
        if OpenClipboard(owner).is_ok() {
            return true;
        }
        Sleep(15);
    }
    false
}

unsafe fn available(format: u32) -> bool {
    format != 0 && IsClipboardFormatAvailable(format).is_ok()
}

/// Reads clipboard text, skipping content that password managers mark as private.
pub fn read_text(owner: HWND) -> Option<String> {
    unsafe {
        if !available(CF_UNICODETEXT) {
            return None;
        }
        let exclude = RegisterClipboardFormatW(w!("ExcludeClipboardContentFromMonitorProcessing"));
        let ignore = RegisterClipboardFormatW(w!("Clipboard Viewer Ignore"));
        let can_history = RegisterClipboardFormatW(w!("CanIncludeInClipboardHistory"));
        if available(exclude) || available(ignore) {
            return None;
        }
        if !open(owner) {
            return None;
        }
        let text = (|| {
            if available(can_history) {
                if let Ok(h) = GetClipboardData(can_history) {
                    let g = HGLOBAL(h.0);
                    let p = GlobalLock(g) as *const u32;
                    if !p.is_null() {
                        let allowed = *p != 0;
                        let _ = GlobalUnlock(g);
                        if !allowed {
                            return None;
                        }
                    }
                }
            }
            let h = GetClipboardData(CF_UNICODETEXT).ok()?;
            let g = HGLOBAL(h.0);
            let p = GlobalLock(g) as *const u16;
            if p.is_null() {
                return None;
            }
            let max = GlobalSize(g) / 2;
            let mut len = 0;
            while len < max && *p.add(len) != 0 {
                len += 1;
            }
            let s = String::from_utf16_lossy(std::slice::from_raw_parts(p, len));
            let _ = GlobalUnlock(g);
            Some(s)
        })();
        let _ = CloseClipboard();
        text.filter(|t| !t.trim().is_empty() && t.chars().count() <= MAX_TEXT_CHARS)
    }
}

pub fn write_text(owner: HWND, text: &str) -> bool {
    let data = crate::util::wide(text);
    unsafe {
        if !open(owner) {
            return false;
        }
        let ok = (|| {
            if EmptyClipboard().is_err() {
                return false;
            }
            let Ok(g) = GlobalAlloc(GMEM_MOVEABLE, data.len() * 2) else { return false };
            let p = GlobalLock(g) as *mut u16;
            if p.is_null() {
                let _ = GlobalFree(g);
                return false;
            }
            std::ptr::copy_nonoverlapping(data.as_ptr(), p, data.len());
            let _ = GlobalUnlock(g);
            if SetClipboardData(CF_UNICODETEXT, HANDLE(g.0)).is_err() {
                let _ = GlobalFree(g);
                return false;
            }
            true
        })();
        let _ = CloseClipboard();
        ok
    }
}
