//! Process-wide state. Everything runs on the UI thread, so a thread-local RefCell is enough.
//! Never keep a borrow across calls that pump messages (menus, CreateWindow, MessageBox):
//! window procedures re-enter and borrow again.

use std::cell::RefCell;
use std::collections::VecDeque;

use windows::Win32::Foundation::{HINSTANCE, HWND};
use windows::Win32::Graphics::Gdi::HFONT;
use windows::Win32::UI::WindowsAndMessaging::HICON;

use crate::config::Config;
use crate::popup::{Item, Layout};

/// Clipboard entries kept in memory (never written to disk).
pub const HISTORY_LIMIT: usize = 50;

#[derive(Default)]
pub struct App {
    pub cfg: Config,
    pub history: VecDeque<String>,
    pub hinst: HINSTANCE,
    pub main: HWND,
    pub popup: HWND,
    pub settings: HWND,
    pub settings_font: HFONT,
    pub icon_on: HICON,
    pub icon_off: HICON,
    pub layout: Option<Layout>,
    /// Item chosen in the ring, pasted a moment after Win is released.
    pub pending: Option<Item>,
    /// Clipboard sequence number of our own write, so it is not recorded as new history.
    pub own_seq: u32,
}

impl App {
    pub fn remember(&mut self, text: String) {
        self.history.retain(|t| *t != text);
        self.history.push_front(text);
        self.history.truncate(HISTORY_LIMIT);
    }

    /// Own words first (always all of them), then the freshest clipboard entries.
    pub fn ring_items(&self) -> Vec<Item> {
        let mut items: Vec<Item> =
            self.cfg.words.iter().map(|w| Item { text: w.clone(), pinned: true }).collect();
        let room = self.cfg.max_items.saturating_sub(items.len());
        let fresh = self
            .history
            .iter()
            .filter(|t| !self.cfg.words.iter().any(|w| w == *t))
            .take(room)
            .map(|t| Item { text: t.clone(), pinned: false });
        items.extend(fresh);
        items
    }
}

thread_local! {
    static APP: RefCell<App> = RefCell::new(App::default());
}

pub fn with<R>(f: impl FnOnce(&mut App) -> R) -> R {
    APP.with(|a| f(&mut a.borrow_mut()))
}

/// For WM_PAINT, which may arrive while some other code holds the state.
pub fn try_with<R>(f: impl FnOnce(&App) -> R) -> Option<R> {
    APP.with(|a| a.try_borrow().ok().map(|a| f(&a)))
}
