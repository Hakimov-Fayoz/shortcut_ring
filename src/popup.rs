//! The ring: a click-through, never-focused layered window around the cursor.
//! Items sit on an ellipse; the one in the direction the cursor moved is selected.

use std::f64::consts::PI;

use windows::Win32::Foundation::{HWND, POINT, RECT, SIZE};
use windows::Win32::Graphics::Gdi::{
    BeginPaint, BitBlt, CreateCompatibleBitmap, CreateCompatibleDC, CreateFontIndirectW, CreatePen,
    CreateSolidBrush, DeleteDC, DeleteObject, DrawTextW, Ellipse, EndPaint, FillRect, GetDC, GetMonitorInfoW,
    GetTextExtentPoint32W, InvalidateRect, MonitorFromPoint, ReleaseDC, RoundRect, SelectObject, SetBkMode,
    SetTextColor, CLEARTYPE_QUALITY, DRAW_TEXT_FORMAT, DT_CALCRECT, DT_CENTER, DT_EDITCONTROL, DT_END_ELLIPSIS, DT_NOPREFIX,
    DT_SINGLELINE, DT_VCENTER, DT_WORDBREAK, FW_NORMAL, FW_SEMIBOLD, HDC, HFONT, HGDIOBJ, LOGFONTW,
    MONITORINFO, MONITOR_DEFAULTTONEAREST, PAINTSTRUCT, PS_SOLID, SRCCOPY, TRANSPARENT,
};
use windows::Win32::UI::HiDpi::{GetDpiForMonitor, MDT_EFFECTIVE_DPI};
use windows::Win32::UI::WindowsAndMessaging::{
    GetCursorPos, KillTimer, SetLayeredWindowAttributes, SetTimer, SetWindowPos, ShowWindow, HWND_TOPMOST,
    LWA_ALPHA, LWA_COLORKEY, SWP_NOACTIVATE, SWP_SHOWWINDOW, SW_HIDE,
};

use crate::app;
use crate::util::{fill_wide, one_line, rgb, utf16};
use crate::TIMER_TRACK;

/// Pixels of this colour are fully transparent.
const KEY: windows::Win32::Foundation::COLORREF = rgb(255, 0, 255);
const OPACITY: u8 = 242;

const PANEL: (u8, u8, u8) = (30, 34, 40);
const PANEL_EDGE: (u8, u8, u8) = (58, 63, 71);
const CHIP: (u8, u8, u8) = (43, 47, 54);
const CHIP_EDGE: (u8, u8, u8) = (70, 76, 86);
const CHIP_TEXT: (u8, u8, u8) = (230, 232, 235);
const PIN: (u8, u8, u8) = (22, 58, 60);
const PIN_EDGE: (u8, u8, u8) = (15, 110, 115);
const PIN_TEXT: (u8, u8, u8) = (127, 211, 207);
const HOVER: (u8, u8, u8) = (15, 110, 115);
const HOVER_EDGE: (u8, u8, u8) = (127, 211, 207);
const HOVER_TEXT: (u8, u8, u8) = (255, 255, 255);
const HINT: (u8, u8, u8) = (150, 158, 170);

#[derive(Clone)]
pub struct Item {
    pub text: String,
    /// One of the user's own words from the settings.
    pub pinned: bool,
}

struct Chip {
    rect: RECT,
    theta: f64,
    label: Vec<u16>,
}

pub struct Layout {
    items: Vec<Item>,
    chips: Vec<Chip>,
    /// Window position on screen and size.
    origin: POINT,
    w: i32,
    h: i32,
    /// Ellipse centre and radii, window coordinates.
    cx: f64,
    cy: f64,
    rx: f64,
    ry: f64,
    /// Where the cursor was when the ring opened, window coordinates.
    start: (f64, f64),
    dead_zone: f64,
    preview: RECT,
    radius: i32,
    font: HFONT,
    font_small: HFONT,
    hover: Option<usize>,
}

impl Drop for Layout {
    fn drop(&mut self) {
        unsafe {
            let _ = DeleteObject(HGDIOBJ(self.font.0));
            let _ = DeleteObject(HGDIOBJ(self.font_small.0));
        }
    }
}

fn font(px: i32, weight: i32) -> HFONT {
    let mut lf = LOGFONTW { lfHeight: -px, lfWeight: weight, lfQuality: CLEARTYPE_QUALITY, ..Default::default() };
    fill_wide(&mut lf.lfFaceName, "Segoe UI");
    unsafe { CreateFontIndirectW(&lf) }
}

fn color((r, g, b): (u8, u8, u8)) -> windows::Win32::Foundation::COLORREF {
    rgb(r, g, b)
}

pub fn init_window(hwnd: HWND) {
    unsafe {
        let _ = SetLayeredWindowAttributes(hwnd, KEY, OPACITY, LWA_COLORKEY | LWA_ALPHA);
    }
}

/// Builds the ring at the cursor and shows it. Always shows something, even an empty
/// hint, so the user sees that holding Win was noticed.
pub fn show() {
    let (items, popup, main) = app::with(|a| (a.ring_items(), a.popup, a.main));
    let layout = unsafe { build(items) };
    let (origin, w, h) = (layout.origin, layout.w, layout.h);
    app::with(|a| a.layout = Some(layout));
    unsafe {
        let _ = SetWindowPos(popup, HWND_TOPMOST, origin.x, origin.y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        let _ = InvalidateRect(popup, None, false);
        SetTimer(main, TIMER_TRACK, 15, None);
    }
}

pub fn hide() {
    let (popup, main) = app::with(|a| (a.popup, a.main));
    unsafe {
        let _ = KillTimer(main, TIMER_TRACK);
        let _ = ShowWindow(popup, SW_HIDE);
    }
    app::with(|a| a.layout = None);
}

/// The item under the cursor right now (called when Win is released).
pub fn selection() -> Option<Item> {
    app::with(|a| a.layout.as_ref().and_then(|l| l.hover.map(|i| l.items[i].clone())))
}

/// Called every 15 ms while the ring is open.
pub fn track() {
    let mut pt = POINT::default();
    unsafe {
        let _ = GetCursorPos(&mut pt);
    }
    let changed = app::with(|a| {
        let Some(l) = a.layout.as_mut() else { return None };
        let hover = l.hit((pt.x - l.origin.x) as f64, (pt.y - l.origin.y) as f64);
        (hover != l.hover).then(|| {
            l.hover = hover;
            a.popup
        })
    });
    if let Some(popup) = changed {
        unsafe {
            let _ = InvalidateRect(popup, None, false);
        }
    }
}

impl Layout {
    fn hit(&self, x: f64, y: f64) -> Option<usize> {
        if let Some(i) = self.chips.iter().position(|c| {
            let r = c.rect;
            x >= r.left as f64 && x < r.right as f64 && y >= r.top as f64 && y < r.bottom as f64
        }) {
            return Some(i);
        }
        if self.chips.is_empty() || (x - self.start.0).hypot(y - self.start.1) < self.dead_zone {
            return None;
        }
        // Direction on the ellipse, then the item whose angle is closest.
        let phi = ((y - self.cy) / self.ry).atan2((x - self.cx) / self.rx);
        let dist = |t: f64| {
            let d = (phi - t).rem_euclid(2.0 * PI);
            d.min(2.0 * PI - d)
        };
        (0..self.chips.len()).min_by(|&a, &b| dist(self.chips[a].theta).total_cmp(&dist(self.chips[b].theta)))
    }
}

unsafe fn build(items: Vec<Item>) -> Layout {
    let mut cursor = POINT::default();
    let _ = GetCursorPos(&mut cursor);
    let monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
    let mut mi = MONITORINFO { cbSize: std::mem::size_of::<MONITORINFO>() as u32, ..Default::default() };
    let _ = GetMonitorInfoW(monitor, &mut mi);
    let (mut dpi_x, mut dpi_y) = (96u32, 96u32);
    let _ = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, &mut dpi_x, &mut dpi_y);
    let s = dpi_x as f64 / 96.0;
    let px = |v: f64| (v * s).round();

    let font_main = font(px(15.0) as i32, FW_SEMIBOLD.0 as i32);
    let font_small = font(px(14.0) as i32, FW_NORMAL.0 as i32);

    // Measure the labels.
    let dc = GetDC(None);
    let old = SelectObject(dc, HGDIOBJ(font_main.0));
    let (pad_x, pad_y) = (px(14.0), px(8.0));
    let (min_w, max_w) = (px(60.0), px(230.0));
    let mut sizes = Vec::with_capacity(items.len());
    let mut labels = Vec::with_capacity(items.len());
    for item in &items {
        let prefix = if item.pinned { "★ " } else { "" };
        let label = utf16(&format!("{prefix}{}", one_line(&item.text, 40)));
        let mut size = SIZE::default();
        let _ = GetTextExtentPoint32W(dc, &label, &mut size);
        sizes.push(((size.cx as f64 + 2.0 * pad_x).clamp(min_w, max_w), size.cy as f64 + 2.0 * pad_y));
        labels.push(label);
    }
    let chip_h = sizes.first().map(|s| s.1).unwrap_or(px(36.0));
    SelectObject(dc, old);
    ReleaseDC(None, dc);

    // Ellipse big enough that neighbours do not overlap: horizontally at the top and
    // bottom (chips are wide), vertically at the sides (chips are short).
    let n = items.len();
    let widest = sizes.iter().map(|s| s.0).fold(min_w, f64::max);
    let step = if n > 0 { 2.0 * PI / n as f64 } else { PI };
    let rx = px(190.0).max((widest * 0.9 + px(12.0)) / step.min(PI / 2.0));
    let ry = px(125.0).max((chip_h + px(10.0)) / step.min(PI / 2.0)).max(rx * 0.42);

    let margin = px(8.0);
    let w = (2.0 * (rx + widest / 2.0 + margin)).ceil() as i32;
    let h = (2.0 * (ry + chip_h / 2.0 + margin)).ceil() as i32;

    // Keep the whole ring on the monitor; near an edge it shifts away from the cursor.
    let wa = mi.rcWork;
    let place = |c: i32, size: i32, lo: i32, hi: i32| {
        if size >= hi - lo { lo } else { (c - size / 2).clamp(lo, hi - size) }
    };
    let origin = POINT { x: place(cursor.x, w, wa.left, wa.right), y: place(cursor.y, h, wa.top, wa.bottom) };
    let (cx, cy) = (w as f64 / 2.0, h as f64 / 2.0);

    let chips = labels
        .into_iter()
        .zip(&sizes)
        .enumerate()
        .map(|(i, (label, &(cw, ch)))| {
            let theta = -PI / 2.0 + i as f64 * step;
            let (x, y) = (cx + rx * theta.cos(), cy + ry * theta.sin());
            let rect = RECT {
                left: (x - cw / 2.0).round() as i32,
                top: (y - ch / 2.0).round() as i32,
                right: (x + cw / 2.0).round() as i32,
                bottom: (y + ch / 2.0).round() as i32,
            };
            Chip { rect, theta, label }
        })
        .collect();

    let pw = (2.0 * rx - widest - px(24.0)).clamp(px(170.0), px(440.0));
    let ph = (2.0 * ry - chip_h - px(28.0)).clamp(px(64.0), px(210.0));
    let preview = RECT {
        left: (cx - pw / 2.0) as i32,
        top: (cy - ph / 2.0) as i32,
        right: (cx + pw / 2.0) as i32,
        bottom: (cy + ph / 2.0) as i32,
    };

    Layout {
        items,
        chips,
        origin,
        w,
        h,
        cx,
        cy,
        rx,
        ry,
        start: ((cursor.x - origin.x) as f64, (cursor.y - origin.y) as f64),
        dead_zone: px(30.0),
        preview,
        radius: px(14.0) as i32,
        font: font_main,
        font_small,
        hover: None,
    }
}

pub fn paint(hwnd: HWND) {
    unsafe {
        let mut ps = PAINTSTRUCT::default();
        let hdc = BeginPaint(hwnd, &mut ps);
        app::try_with(|a| {
            if let Some(l) = &a.layout {
                let mem = CreateCompatibleDC(hdc);
                let bmp = CreateCompatibleBitmap(hdc, l.w, l.h);
                let old = SelectObject(mem, HGDIOBJ(bmp.0));
                draw(l, mem);
                let _ = BitBlt(hdc, 0, 0, l.w, l.h, mem, 0, 0, SRCCOPY);
                SelectObject(mem, old);
                let _ = DeleteObject(HGDIOBJ(bmp.0));
                let _ = DeleteDC(mem);
            }
        });
        let _ = EndPaint(hwnd, &ps);
    }
}

unsafe fn shape(dc: HDC, fill: (u8, u8, u8), edge: (u8, u8, u8), f: impl FnOnce()) {
    let brush = CreateSolidBrush(color(fill));
    let pen = CreatePen(PS_SOLID, 1, color(edge));
    let old_b = SelectObject(dc, HGDIOBJ(brush.0));
    let old_p = SelectObject(dc, HGDIOBJ(pen.0));
    f();
    SelectObject(dc, old_b);
    SelectObject(dc, old_p);
    let _ = DeleteObject(HGDIOBJ(brush.0));
    let _ = DeleteObject(HGDIOBJ(pen.0));
}

unsafe fn text(dc: HDC, font: HFONT, rgb_: (u8, u8, u8), s: &str, mut r: RECT, fmt: DRAW_TEXT_FORMAT) {
    let old = SelectObject(dc, HGDIOBJ(font.0));
    SetTextColor(dc, color(rgb_));
    let mut buf = utf16(s);
    DrawTextW(dc, &mut buf, &mut r, fmt | DT_NOPREFIX);
    SelectObject(dc, old);
}

unsafe fn draw(l: &Layout, dc: HDC) {
    let all = RECT { left: 0, top: 0, right: l.w, bottom: l.h };
    let key = CreateSolidBrush(KEY);
    FillRect(dc, &all, key);
    let _ = DeleteObject(HGDIOBJ(key.0));
    SetBkMode(dc, TRANSPARENT);

    // Centre panel: full text of the hovered item, or a hint.
    let p = l.preview;
    shape(dc, PANEL, PANEL_EDGE, || {
        let _ = RoundRect(dc, p.left, p.top, p.right, p.bottom, l.radius * 2, l.radius * 2);
    });
    let pad = l.radius;
    let inner = RECT { left: p.left + pad, top: p.top + pad / 2, right: p.right - pad, bottom: p.bottom - pad / 2 };
    match l.hover {
        Some(i) => {
            let item = &l.items[i];
            let title_h = (inner.bottom - inner.top).min(l.radius * 2);
            let (title, tint) =
                if item.pinned { ("★ Своё слово", PIN_TEXT) } else { ("Из буфера обмена", HINT) };
            let title_r = RECT { bottom: inner.top + title_h, ..inner };
            text(dc, l.font_small, tint, title, title_r, DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
            let body: String = item.text.replace('\t', "    ").chars().take(1500).collect();
            let body_r = RECT { top: inner.top + title_h, ..inner };
            text(dc, l.font_small, CHIP_TEXT, &body, body_r, DT_WORDBREAK | DT_EDITCONTROL | DT_END_ELLIPSIS);
        }
        None => {
            let hint = if l.items.is_empty() {
                "Буфер обмена пуст.\nСкопируйте что-нибудь или добавьте свои слова в настройках."
            } else {
                "Наведите на элемент и отпустите Win.\nEsc — закрыть без вставки."
            };
            let mut probe = inner;
            let mut buf = utf16(hint);
            let old = SelectObject(dc, HGDIOBJ(l.font_small.0));
            let th = DrawTextW(dc, &mut buf, &mut probe, DT_WORDBREAK | DT_CENTER | DT_NOPREFIX | DT_CALCRECT);
            SelectObject(dc, old);
            let top = inner.top + ((inner.bottom - inner.top) - th).max(0) / 2;
            let r = RECT { top, ..inner };
            text(dc, l.font_small, HINT, hint, r, DT_WORDBREAK | DT_CENTER);
        }
    }

    // A dot where the cursor started: moving away from it picks a direction.
    let (sx, sy, d) = (l.start.0 as i32, l.start.1 as i32, (l.dead_zone / 6.0).max(3.0) as i32);
    shape(dc, HOVER_EDGE, HOVER_EDGE, || {
        let _ = Ellipse(dc, sx - d, sy - d, sx + d, sy + d);
    });

    for (i, c) in l.chips.iter().enumerate() {
        let item = &l.items[i];
        let (fill, edge, ink) = if l.hover == Some(i) {
            (HOVER, HOVER_EDGE, HOVER_TEXT)
        } else if item.pinned {
            (PIN, PIN_EDGE, PIN_TEXT)
        } else {
            (CHIP, CHIP_EDGE, CHIP_TEXT)
        };
        let r = c.rect;
        let round = r.bottom - r.top;
        shape(dc, fill, edge, || {
            let _ = RoundRect(dc, r.left, r.top, r.right, r.bottom, round, round);
        });
        let inner = RECT { left: r.left + l.radius / 2, right: r.right - l.radius / 2, ..r };
        let old = SelectObject(dc, HGDIOBJ(l.font.0));
        SetTextColor(dc, color(ink));
        let mut label = c.label.clone();
        let mut ir = inner;
        DrawTextW(dc, &mut label, &mut ir, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS | DT_NOPREFIX);
        SelectObject(dc, old);
    }
}
