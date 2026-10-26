//! «Запускать вместе с Windows» = a value under HKCU\...\Run (no admin rights needed).

use windows::core::{w, PCWSTR};
use windows::Win32::System::Registry::{
    RegCloseKey, RegCreateKeyExW, RegDeleteValueW, RegOpenKeyExW, RegQueryValueExW, RegSetValueExW, HKEY,
    HKEY_CURRENT_USER, KEY_QUERY_VALUE, KEY_SET_VALUE, REG_OPTION_NON_VOLATILE, REG_SZ,
};

const RUN: PCWSTR = w!("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
const NAME: PCWSTR = w!("ClipRing");

pub fn is_enabled() -> bool {
    unsafe {
        let mut key = HKEY::default();
        if RegOpenKeyExW(HKEY_CURRENT_USER, RUN, 0, KEY_QUERY_VALUE, &mut key).is_err() {
            return false;
        }
        let found = RegQueryValueExW(key, NAME, None, None, None, None).is_ok();
        let _ = RegCloseKey(key);
        found
    }
}

pub fn set(on: bool) {
    unsafe {
        let mut key = HKEY::default();
        if RegCreateKeyExW(HKEY_CURRENT_USER, RUN, 0, PCWSTR::null(), REG_OPTION_NON_VOLATILE, KEY_SET_VALUE, None, &mut key, None)
            .is_err()
        {
            return;
        }
        if on {
            if let Ok(exe) = std::env::current_exe() {
                let value = crate::util::wide(&format!("\"{}\"", exe.display()));
                let bytes = std::slice::from_raw_parts(value.as_ptr() as *const u8, value.len() * 2);
                let _ = RegSetValueExW(key, NAME, 0, REG_SZ, Some(bytes));
            }
        } else {
            let _ = RegDeleteValueW(key, NAME);
        }
        let _ = RegCloseKey(key);
    }
}
