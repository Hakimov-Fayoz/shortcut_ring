use serde::{Deserialize, Serialize};
use std::path::PathBuf;

pub const HOLD_MS_RANGE: (u32, u32) = (100, 3000);
pub const MAX_ITEMS_RANGE: (usize, usize) = (1, 24);

#[derive(Clone, Serialize, Deserialize)]
#[serde(default)]
pub struct Config {
    pub enabled: bool,
    /// Words and phrases that are always shown in the ring, even when not in the clipboard.
    pub words: Vec<String>,
    /// How long Win must be held alone before the ring appears.
    pub hold_ms: u32,
    /// How many items the ring shows (own words are always shown on top of this).
    pub max_items: usize,
}

impl Default for Config {
    fn default() -> Self {
        Self { enabled: true, words: Vec::new(), hold_ms: 350, max_items: 12 }
    }
}

impl Config {
    pub fn path() -> PathBuf {
        let base = std::env::var_os("APPDATA").map(PathBuf::from).unwrap_or_else(|| ".".into());
        base.join("ClipRing").join("config.json")
    }

    /// Returns the config and whether the file already existed (false = first run).
    pub fn load() -> (Self, bool) {
        match std::fs::read_to_string(Self::path()) {
            // Notepad and PowerShell like to save UTF-8 with a BOM, which serde_json rejects.
            Ok(text) => {
                let json = text.trim_start_matches('\u{feff}');
                (serde_json::from_str::<Config>(json).unwrap_or_default().clamped(), true)
            }
            Err(_) => (Self::default(), false),
        }
    }

    pub fn save(&self) -> std::io::Result<()> {
        let path = Self::path();
        if let Some(dir) = path.parent() {
            std::fs::create_dir_all(dir)?;
        }
        let json = serde_json::to_string_pretty(self).map_err(std::io::Error::other)?;
        std::fs::write(path, json)
    }

    pub fn clamped(mut self) -> Self {
        self.hold_ms = self.hold_ms.clamp(HOLD_MS_RANGE.0, HOLD_MS_RANGE.1);
        self.max_items = self.max_items.clamp(MAX_ITEMS_RANGE.0, MAX_ITEMS_RANGE.1);
        self.words.retain(|w| !w.trim().is_empty());
        self
    }
}
