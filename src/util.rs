use windows::Win32::Foundation::COLORREF;

/// UTF-16 with a trailing NUL, for passing to the Win32 API.
pub fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

/// UTF-16 without a trailing NUL, for DrawTextW / GetTextExtentPoint32W.
pub fn utf16(s: &str) -> Vec<u16> {
    s.encode_utf16().collect()
}

pub const fn rgb(r: u8, g: u8, b: u8) -> COLORREF {
    COLORREF(r as u32 | (g as u32) << 8 | (b as u32) << 16)
}

/// Copies `s` into a fixed-size UTF-16 buffer (tray tooltip, font face name), truncating.
pub fn fill_wide(dst: &mut [u16], s: &str) {
    let src = utf16(s);
    let n = src.len().min(dst.len() - 1);
    dst[..n].copy_from_slice(&src[..n]);
    dst[n] = 0;
}

/// One-line label for a chip: line breaks become ↵, long text is cut with an ellipsis.
pub fn one_line(text: &str, max_chars: usize) -> String {
    let flat = text
        .trim()
        .replace("\r\n", " ↵ ")
        .replace(['\n', '\r'], " ↵ ")
        .replace('\t', " ");
    let mut out: String = flat.chars().take(max_chars).collect();
    if flat.chars().count() > max_chars {
        out.push('…');
    }
    out
}
