//! wade: TUI file browser (Rust port of the C# Native AOT tool).

use std::path::Path;

use wade::app::input_reader::AppAction;
use wade::app::{App, AppConfig};
use wade::fs::directory_contents::SortMode;
use wade::input::CancelToken;

fn main() {
    // C# WadeConfig.Load: config file first, then args
    let mut config = load_config();

    // C# arg convention: the first non-flag arg is the start path.
    let args: Vec<String> = std::env::args().skip(1).collect();
    for arg in &args {
        if arg.starts_with("--config-file=") {
            continue;
        }

        let mut start = arg.clone();
        if start.starts_with('~')
            && let Some(home) = std::env::var_os("USERPROFILE").or_else(|| std::env::var_os("HOME")) {
                start = Path::new(&home).join(&start[1..]).to_string_lossy().to_string();
            }

        config.start_path = start;
        break;
    }

    let mut app = App::new(config);
    let mut source = wade::input::windows::WindowsInputSource::new();
    let cancel = CancelToken::new();

    let result = app.run(&mut source, &cancel);

    // C# writes the final directory to stdout for shell integration
    if let Some(cwd) = result {
        println!("{cwd}");
    }

    let _ = AppAction::None; // keep enum import referenced for docs builds
}

/// Port of the `WadeConfig.Load` subset: `~/.config/wade/config.toml`,
/// `key = value` lines with `#` comments; keys match C# exactly.
#[must_use]
fn load_config() -> AppConfig {
    let mut config = AppConfig::default();

    let Some(home) = std::env::var_os("USERPROFILE").or_else(|| std::env::var_os("HOME")) else {
        return config;
    };

    let config_path = Path::new(&home).join(".config").join("wade").join("config.toml");
    let Ok(text) = std::fs::read_to_string(&config_path) else {
        return config;
    };

    for raw_line in text.lines() {
        let trimmed = raw_line.trim();
        if trimmed.is_empty() || trimmed.starts_with('#') {
            continue;
        }

        let Some(eq) = trimmed.find('=') else {
            continue;
        };

        let key = trimmed[..eq].trim();
        let mut value = trimmed[eq + 1..].trim();

        // Strip inline comment
        if let Some(idx) = value.find('#') {
            value = value[..idx].trim();
        }

        match key {
            "show_icons_enabled" => config.show_icons_enabled = parse_bool(value, config.show_icons_enabled),
            "show_hidden_files" => config.show_hidden_files = parse_bool(value, config.show_hidden_files),
            "show_system_files" => config.show_system_files = parse_bool(value, config.show_system_files),
            "sort_mode" => {
                if let Some(mode) = parse_sort_mode(value) {
                    config.sort_mode = mode;
                }
            }
            "sort_ascending" => config.sort_ascending = parse_bool(value, config.sort_ascending),
            "parent_pane_enabled" => config.parent_pane_enabled = parse_bool(value, config.parent_pane_enabled),
            "preview_pane_enabled" => config.preview_pane_enabled = parse_bool(value, config.preview_pane_enabled),
            "size_column_enabled" => config.size_column_enabled = parse_bool(value, config.size_column_enabled),
            "date_column_enabled" => config.date_column_enabled = parse_bool(value, config.date_column_enabled),
            "column_headers_enabled" => config.column_headers_enabled = parse_bool(value, config.column_headers_enabled),
            _ => {}
        }
    }

    config
}

fn parse_bool(value: &str, default: bool) -> bool {
    match value.to_ascii_lowercase().as_str() {
        "true" => true,
        "false" => false,
        _ => default,
    }
}

fn parse_sort_mode(value: &str) -> Option<SortMode> {
    match value.to_ascii_lowercase().as_str() {
        "name" => Some(SortMode::Name),
        "modified" => Some(SortMode::Modified),
        "size" => Some(SortMode::Size),
        "extension" => Some(SortMode::Extension),
        _ => None,
    }
}
