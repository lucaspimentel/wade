//! Port of `WadeConfig.Load` / `WadeConfig.Save` (src/Wade/WadeConfig.cs):
//! `~/.config/wade/config.toml`, `key = value` lines with `#` comments; keys
//! match C# exactly.

use std::path::{Path, PathBuf};

use crate::app::AppConfig;
use crate::fs::directory_contents::SortMode;

fn default_config_path() -> Option<PathBuf> {
    let home = std::env::var_os("USERPROFILE").or_else(|| std::env::var_os("HOME"))?;
    Some(
        Path::new(&home)
            .join(".config")
            .join("wade")
            .join("config.toml"),
    )
}

/// The path `save_config` writes to: the `--config-file=` override when the
/// config was loaded with one, otherwise the default location.
#[must_use]
pub fn config_path(config: &AppConfig) -> PathBuf {
    config
        .config_file_path
        .as_ref()
        .map_or_else(
            || default_config_path().unwrap_or_else(|| PathBuf::from("config.toml")),
            PathBuf::from,
        )
}

/// Port of `WadeConfig.Load`.
pub fn load_config(args: &[String]) -> AppConfig {
    let mut config = AppConfig::default();

    // Allow --config-file=<path> to override the config file location
    let mut config_file_path = default_config_path();
    for arg in args {
        if let Some(path) = arg.strip_prefix("--config-file=") {
            config_file_path = Some(PathBuf::from(path));
            break;
        }
    }

    config.config_file_path = config_file_path.as_ref().map(|p| p.to_string_lossy().to_string());

    let Some(config_path) = config_file_path else {
        return config;
    };
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
            "image_previews_enabled" => config.image_previews_enabled = parse_bool(value, config.image_previews_enabled),
            "image_protocol" => {
                if let Some(setting) = crate::imaging::ImageProtocolSetting::parse(value) {
                    config.image_protocol = setting;
                }
            }
            "show_hidden_files" => config.show_hidden_files = parse_bool(value, config.show_hidden_files),
            "show_system_files" => config.show_system_files = parse_bool(value, config.show_system_files),
            "sort_mode" => {
                if let Some(mode) = parse_sort_mode(value) {
                    config.sort_mode = mode;
                }
            }
            "sort_ascending" => config.sort_ascending = parse_bool(value, config.sort_ascending),
            "confirm_delete_enabled" => config.confirm_delete_enabled = parse_bool(value, config.confirm_delete_enabled),
            "parent_pane_enabled" => config.parent_pane_enabled = parse_bool(value, config.parent_pane_enabled),
            "preview_pane_enabled" => config.preview_pane_enabled = parse_bool(value, config.preview_pane_enabled),
            "size_column_enabled" => config.size_column_enabled = parse_bool(value, config.size_column_enabled),
            "date_column_enabled" => config.date_column_enabled = parse_bool(value, config.date_column_enabled),
            "column_headers_enabled" => config.column_headers_enabled = parse_bool(value, config.column_headers_enabled),
            "copy_symlinks_as_links_enabled" => {
                config.copy_symlinks_as_links_enabled = parse_bool(value, config.copy_symlinks_as_links_enabled);
            }
            "zip_preview_enabled" => config.zip_preview_enabled = parse_bool(value, config.zip_preview_enabled),
            "terminal_title_enabled" => config.terminal_title_enabled = parse_bool(value, config.terminal_title_enabled),
            "git_status_enabled" => config.git_status_enabled = parse_bool(value, config.git_status_enabled),
            "file_metadata_enabled" => config.file_metadata_enabled = parse_bool(value, config.file_metadata_enabled),
            "file_previews_enabled" => config.file_previews_enabled = parse_bool(value, config.file_previews_enabled),
            "archive_metadata_enabled" => config.archive_metadata_enabled = parse_bool(value, config.archive_metadata_enabled),
            "dir_size_ssd_enabled" => config.dir_size_ssd_enabled = parse_bool(value, config.dir_size_ssd_enabled),
            "dir_size_hdd_enabled" => config.dir_size_hdd_enabled = parse_bool(value, config.dir_size_hdd_enabled),
            "dir_size_network_enabled" => config.dir_size_network_enabled = parse_bool(value, config.dir_size_network_enabled),
            "pdf_preview_enabled" => config.pdf_preview_enabled = parse_bool(value, config.pdf_preview_enabled),
            "pdf_metadata_enabled" => config.pdf_metadata_enabled = parse_bool(value, config.pdf_metadata_enabled),
            "markdown_preview_enabled" => config.markdown_preview_enabled = parse_bool(value, config.markdown_preview_enabled),
            "ffprobe_enabled" => config.ffprobe_enabled = parse_bool(value, config.ffprobe_enabled),
            "mediainfo_enabled" => config.mediainfo_enabled = parse_bool(value, config.mediainfo_enabled),
            "detail_columns_enabled" => {
                // Backward compat: sets both columns
                let detail = parse_bool(value, true);
                config.size_column_enabled = detail;
                config.date_column_enabled = detail;
            }
            "disabled_tools" => {
                for tool in value.split(',').map(str::trim).filter(|tool| !tool.is_empty()) {
                    match tool {
                        "pdftopng" => config.pdf_preview_enabled = false,
                        "pdfinfo" => config.pdf_metadata_enabled = false,
                        "markdown_preview" => config.markdown_preview_enabled = false,
                        "ffprobe" => config.ffprobe_enabled = false,
                        "mediainfo" => config.mediainfo_enabled = false,
                        _ => {}
                    }
                }
            }
            _ => {}
        }
    }

    config
}

/// The CLI half of `WadeConfig.Load`: `--cwd-file=`, `--show-config`,
/// `--help`/`-h`, `--version`, and the first non-flag argument as the start
/// path.
pub fn apply_cli_args(config: &mut AppConfig, args: &[String]) {
    for arg in args {
        if let Some(path) = arg.strip_prefix("--cwd-file=") {
            config.cwd_file_path = Some(path.to_string());
            continue;
        }

        match arg.as_str() {
            "--show-config" => config.show_config = true,
            "--help" | "-h" => config.show_help = true,
            "--version" => config.show_version = true,
            _ => {}
        }
    }

    if let Some(start) = args.iter().find(|arg| !arg.starts_with('-')) {
        config.start_path = normalize_start_path(start);
    }
}

/// The start-path half of `WadeConfig.Load`: expands a leading `~` to the
/// home directory, then strips trailing separators (both kinds, on every
/// OS) while keeping a root (`/`, `C:\`).
#[must_use]
pub fn normalize_start_path(path: &str) -> String {
    let expanded = if path.starts_with('~') { crate::fs::path_completion::expand_tilde(path) } else { path.to_string() };
    let trimmed = expanded.trim_end_matches(['/', '\\']);

    if trimmed.is_empty() {
        // "/" or "\" -> keep the root
        expanded.chars().next().map(String::from).unwrap_or_default()
    } else if trimmed.len() == 2 && trimmed.as_bytes()[1] == b':' && trimmed.len() < expanded.len() {
        // "C:\" was trimmed to "C:": restore the root separator
        format!("{trimmed}\\")
    } else {
        trimmed.to_string()
    }
}

/// Port of `WadeConfig.ToJson` (`--show-config`).
#[must_use]
pub fn to_json(config: &AppConfig) -> String {
    let flag = |value: bool| if value { "true" } else { "false" };
    let sort_mode = match config.sort_mode {
        SortMode::Name => "name",
        SortMode::Modified => "modified",
        SortMode::Size => "size",
        SortMode::Extension => "extension",
    };
    let fields = [
        ("show_icons_enabled", flag(config.show_icons_enabled)),
        ("image_previews_enabled", flag(config.image_previews_enabled)),
        ("show_hidden_files", flag(config.show_hidden_files)),
        ("show_system_files", flag(config.show_system_files)),
        ("sort_mode", ""),
        ("sort_ascending", flag(config.sort_ascending)),
        ("confirm_delete_enabled", flag(config.confirm_delete_enabled)),
        ("parent_pane_enabled", flag(config.parent_pane_enabled)),
        ("preview_pane_enabled", flag(config.preview_pane_enabled)),
        ("size_column_enabled", flag(config.size_column_enabled)),
        ("date_column_enabled", flag(config.date_column_enabled)),
        ("column_headers_enabled", flag(config.column_headers_enabled)),
        ("copy_symlinks_as_links_enabled", flag(config.copy_symlinks_as_links_enabled)),
        ("zip_preview_enabled", flag(config.zip_preview_enabled)),
        ("terminal_title_enabled", flag(config.terminal_title_enabled)),
        ("git_status_enabled", flag(config.git_status_enabled)),
        ("file_metadata_enabled", flag(config.file_metadata_enabled)),
        ("file_previews_enabled", flag(config.file_previews_enabled)),
        ("archive_metadata_enabled", flag(config.archive_metadata_enabled)),
        ("dir_size_ssd_enabled", flag(config.dir_size_ssd_enabled)),
        ("dir_size_hdd_enabled", flag(config.dir_size_hdd_enabled)),
        ("dir_size_network_enabled", flag(config.dir_size_network_enabled)),
        ("pdf_preview_enabled", flag(config.pdf_preview_enabled)),
        ("pdf_metadata_enabled", flag(config.pdf_metadata_enabled)),
        ("markdown_preview_enabled", flag(config.markdown_preview_enabled)),
        ("ffprobe_enabled", flag(config.ffprobe_enabled)),
        ("mediainfo_enabled", flag(config.mediainfo_enabled)),
    ];

    let mut json = String::from("{");

    for (key, value) in fields {
        if key == "sort_mode" {
            json.push_str(&format!("\"sort_mode\":\"{sort_mode}\","));
        } else {
            json.push_str(&format!("\"{key}\":{value},"));
        }
    }

    // Rust-only key
    json.push_str(&format!("\"image_protocol\":\"{}\",", config.image_protocol.name()));
    // C# escapes only backslashes in the start path
    json.push_str(&format!("\"start_path\":\"{}\"}}", config.start_path.replace('\\', "\\\\")));
    json
}

/// Port of `WadeConfig.Save`: all 27 keys in the C# order.
pub fn save_config(config: &AppConfig) -> std::io::Result<()> {
    let path = config_path(config);
    if let Some(dir) = path.parent() {
        std::fs::create_dir_all(dir)?;
    }

    let sort_mode = match config.sort_mode {
        SortMode::Name => "name",
        SortMode::Modified => "modified",
        SortMode::Size => "size",
        SortMode::Extension => "extension",
    };

    let content = format!(
        "show_icons_enabled = {}\n\
         image_previews_enabled = {}\n\
         show_hidden_files = {}\n\
         show_system_files = {}\n\
         sort_mode = {sort_mode}\n\
         sort_ascending = {}\n\
         confirm_delete_enabled = {}\n\
         parent_pane_enabled = {}\n\
         preview_pane_enabled = {}\n\
         size_column_enabled = {}\n\
         date_column_enabled = {}\n\
         column_headers_enabled = {}\n\
         copy_symlinks_as_links_enabled = {}\n\
         zip_preview_enabled = {}\n\
         terminal_title_enabled = {}\n\
         git_status_enabled = {}\n\
         file_metadata_enabled = {}\n\
         file_previews_enabled = {}\n\
         archive_metadata_enabled = {}\n\
         dir_size_ssd_enabled = {}\n\
         dir_size_hdd_enabled = {}\n\
         dir_size_network_enabled = {}\n\
         pdf_preview_enabled = {}\n\
         pdf_metadata_enabled = {}\n\
         markdown_preview_enabled = {}\n\
         ffprobe_enabled = {}\n\
         mediainfo_enabled = {}\n",
        config.show_icons_enabled,
        config.image_previews_enabled,
        config.show_hidden_files,
        config.show_system_files,
        config.sort_ascending,
        config.confirm_delete_enabled,
        config.parent_pane_enabled,
        config.preview_pane_enabled,
        config.size_column_enabled,
        config.date_column_enabled,
        config.column_headers_enabled,
        config.copy_symlinks_as_links_enabled,
        config.zip_preview_enabled,
        config.terminal_title_enabled,
        config.git_status_enabled,
        config.file_metadata_enabled,
        config.file_previews_enabled,
        config.archive_metadata_enabled,
        config.dir_size_ssd_enabled,
        config.dir_size_hdd_enabled,
        config.dir_size_network_enabled,
        config.pdf_preview_enabled,
        config.pdf_metadata_enabled,
        config.markdown_preview_enabled,
        config.ffprobe_enabled,
        config.mediainfo_enabled,
    );

    // Rust-only key, written only when set so the file keeps the C# layout
    let mut content = content;
    if config.image_protocol != crate::imaging::ImageProtocolSetting::Auto {
        content.push_str(&format!("image_protocol = {}\n", config.image_protocol.name()));
    }

    std::fs::write(path, content)
}

/// Port of `WadeConfig.ParseBool`.
fn parse_bool(value: &str, default: bool) -> bool {
    match value.to_lowercase().as_str() {
        "true" | "1" | "yes" => true,
        "false" | "0" | "no" => false,
        _ => default,
    }
}

/// `Enum.TryParse<SortMode>(value, ignoreCase: true)`: member names or
/// their numeric values (C# would also store undefined numbers; those are
/// ignored here).
fn parse_sort_mode(value: &str) -> Option<SortMode> {
    match value.to_ascii_lowercase().as_str() {
        "name" | "0" => Some(SortMode::Name),
        "modified" | "1" => Some(SortMode::Modified),
        "size" | "2" => Some(SortMode::Size),
        "extension" | "3" => Some(SortMode::Extension),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[expect(clippy::field_reassign_with_default)]
    fn config_with_path(file_name: &str) -> AppConfig {
        let mut config = AppConfig::default();
        config.config_file_path = Some(
            std::env::temp_dir()
                .join(format!("wade-config-test-{}-{file_name}", std::process::id()))
                .to_string_lossy()
                .to_string(),
        );
        config
    }

    /// The saved file must match the C# `WadeConfig.Save()` layout: 27 keys,
    /// exact order, `key = true|false`, lowercase sort mode.
    #[test]
    fn save_writes_exact_csharp_layout() {
        let config = config_with_path("save.txt");
        save_config(&config).expect("save");
        let text = std::fs::read_to_string(config_path(&config)).expect("read back");
        let expected = "\
show_icons_enabled = true
image_previews_enabled = true
show_hidden_files = false
show_system_files = false
sort_mode = name
sort_ascending = true
confirm_delete_enabled = true
parent_pane_enabled = true
preview_pane_enabled = true
size_column_enabled = true
date_column_enabled = true
column_headers_enabled = true
copy_symlinks_as_links_enabled = true
zip_preview_enabled = true
terminal_title_enabled = true
git_status_enabled = true
file_metadata_enabled = true
file_previews_enabled = true
archive_metadata_enabled = true
dir_size_ssd_enabled = true
dir_size_hdd_enabled = false
dir_size_network_enabled = false
pdf_preview_enabled = true
pdf_metadata_enabled = true
markdown_preview_enabled = true
ffprobe_enabled = true
mediainfo_enabled = true
";
        assert_eq!(text, expected);
    }

    #[test]
    fn load_round_trips_saved_config() {
        let mut config = config_with_path("roundtrip.txt");
        config.show_hidden_files = true;
        config.sort_mode = SortMode::Size;
        config.dir_size_hdd_enabled = true;
        save_config(&config).expect("save");

        let args: Vec<String> = vec![format!("--config-file={}", config_path(&config).display())];
        let loaded = load_config(&args);
        assert!(loaded.show_hidden_files);
        assert_eq!(loaded.sort_mode, SortMode::Size);
        assert!(loaded.dir_size_hdd_enabled);
        assert!(loaded.mediainfo_enabled);
        assert_eq!(loaded.config_file_path, config.config_file_path);
    }

    #[test]
    fn cli_args_set_flags_and_first_non_flag_start_path() {
        let mut config = AppConfig::default();
        let args: Vec<String> = ["--cwd-file=/tmp/out", "-h", "--version", "--show-config", "/start", "/ignored"]
            .iter()
            .map(|s| (*s).to_string())
            .collect();
        apply_cli_args(&mut config, &args);
        assert_eq!(config.cwd_file_path.as_deref(), Some("/tmp/out"));
        assert!(config.show_help && config.show_version && config.show_config);
        assert_eq!(config.start_path, "/start");
    }

    #[test]
    fn disabled_tools_and_detail_columns_keys() {
        let config = config_with_path("legacy.toml");
        let path = config.config_file_path.clone().unwrap();
        std::fs::write(&path, "disabled_tools = pdftopng, markdown_preview ,mediainfo\ndetail_columns_enabled = no\n").unwrap();
        let loaded = load_config(&[format!("--config-file={path}")]);
        assert!(!loaded.pdf_preview_enabled && !loaded.markdown_preview_enabled && !loaded.mediainfo_enabled);
        assert!(loaded.pdf_metadata_enabled && loaded.ffprobe_enabled);
        assert!(!loaded.size_column_enabled && !loaded.date_column_enabled);
        let _ = std::fs::remove_file(path);
    }

    #[test]
    fn to_json_escapes_backslashes_only() {
        let config = AppConfig { start_path: "C:\\a \"b\"".to_string(), ..AppConfig::default() };
        let json = to_json(&config);
        assert!(json.starts_with("{\"show_icons_enabled\":true,"));
        assert!(json.contains("\"sort_mode\":\"name\","));
        assert!(json.ends_with("\"start_path\":\"C:\\\\a \"b\"\"}"));
    }

    /// Loads `text` as a config file (port of the WadeConfigTests helper).
    fn load_text(name: &str, text: &str) -> AppConfig {
        let path = config_with_path(name).config_file_path.unwrap();
        std::fs::write(&path, text).unwrap();
        let loaded = load_config(&[format!("--config-file={path}")]);
        let _ = std::fs::remove_file(&path);
        loaded
    }

    type Field = fn(&AppConfig) -> bool;

    /// Every boolean key, with its field and default.
    fn bool_keys() -> Vec<(&'static str, Field, bool)> {
        vec![
            ("show_icons_enabled", |c| c.show_icons_enabled, true),
            ("image_previews_enabled", |c| c.image_previews_enabled, true),
            ("show_hidden_files", |c| c.show_hidden_files, false),
            ("show_system_files", |c| c.show_system_files, false),
            ("sort_ascending", |c| c.sort_ascending, true),
            ("confirm_delete_enabled", |c| c.confirm_delete_enabled, true),
            ("parent_pane_enabled", |c| c.parent_pane_enabled, true),
            ("preview_pane_enabled", |c| c.preview_pane_enabled, true),
            ("size_column_enabled", |c| c.size_column_enabled, true),
            ("date_column_enabled", |c| c.date_column_enabled, true),
            ("column_headers_enabled", |c| c.column_headers_enabled, true),
            ("copy_symlinks_as_links_enabled", |c| c.copy_symlinks_as_links_enabled, true),
            ("zip_preview_enabled", |c| c.zip_preview_enabled, true),
            ("terminal_title_enabled", |c| c.terminal_title_enabled, true),
            ("git_status_enabled", |c| c.git_status_enabled, true),
            ("file_metadata_enabled", |c| c.file_metadata_enabled, true),
            ("file_previews_enabled", |c| c.file_previews_enabled, true),
            ("archive_metadata_enabled", |c| c.archive_metadata_enabled, true),
            ("dir_size_ssd_enabled", |c| c.dir_size_ssd_enabled, true),
            ("dir_size_hdd_enabled", |c| c.dir_size_hdd_enabled, false),
            ("dir_size_network_enabled", |c| c.dir_size_network_enabled, false),
            ("pdf_preview_enabled", |c| c.pdf_preview_enabled, true),
            ("pdf_metadata_enabled", |c| c.pdf_metadata_enabled, true),
            ("markdown_preview_enabled", |c| c.markdown_preview_enabled, true),
            ("ffprobe_enabled", |c| c.ffprobe_enabled, true),
            ("mediainfo_enabled", |c| c.mediainfo_enabled, true),
        ]
    }

    #[test]
    fn defaults_when_nothing_provided() {
        let config = load_config(&["--config-file=/nonexistent/wade/config.toml".to_string()]);
        for (key, field, default) in bool_keys() {
            assert_eq!(field(&config), default, "{key} default");
        }
        assert_eq!(config.sort_mode, SortMode::Name);
        assert!(config.cwd_file_path.is_none());
        assert!(!config.show_config && !config.show_help && !config.show_version);
    }

    #[test]
    fn every_bool_key_parses_both_values() {
        for (key, field, default) in bool_keys() {
            for value in [!default, default] {
                let loaded = load_text(&format!("bool-{key}-{value}"), &format!("{key} = {value}\n"));
                assert_eq!(field(&loaded), value, "{key} = {value}");
            }
        }
    }

    #[test]
    fn parse_bool_accepts_yes_no_and_digits_case_insensitively_and_keeps_the_default_otherwise() {
        for (text, expected) in [("true", true), ("TRUE", true), ("1", true), ("yes", true), ("Yes", true)] {
            assert_eq!(parse_bool(text, false), expected, "{text}");
        }
        for (text, expected) in [("false", false), ("False", false), ("0", false), ("no", false), ("NO", false)] {
            assert_eq!(parse_bool(text, true), expected, "{text}");
        }
        for text in ["", "maybe", "2", "on"] {
            assert!(parse_bool(text, true), "{text} keeps the default");
            assert!(!parse_bool(text, false), "{text} keeps the default");
        }
    }

    #[test]
    fn sort_mode_parses_names_and_numbers_case_insensitively() {
        for (text, expected) in [
            ("name", SortMode::Name),
            ("modified", SortMode::Modified),
            ("size", SortMode::Size),
            ("extension", SortMode::Extension),
            ("Modified", SortMode::Modified),
            ("EXTENSION", SortMode::Extension),
            ("0", SortMode::Name),
            ("1", SortMode::Modified),
            ("2", SortMode::Size),
            ("3", SortMode::Extension),
        ] {
            assert_eq!(load_text("sort", &format!("sort_mode = {text}\n")).sort_mode, expected, "{text}");
        }
        assert_eq!(load_text("sort-bad", "sort_mode = size\nsort_mode = bogus\n").sort_mode, SortMode::Size, "invalid keeps the value");
    }

    #[test]
    fn comments_blank_malformed_and_unknown_lines_are_ignored() {
        let loaded = load_text(
            "lines",
            "# a comment\n\n   \nshow_hidden_files\n= true\nunknown_key = true\n  show_icons_enabled = false   # inline comment\n  # show_hidden_files = true\n",
        );
        assert!(!loaded.show_icons_enabled, "inline comment stripped");
        assert!(!loaded.show_hidden_files, "comment and malformed lines ignored");
    }

    #[test]
    fn missing_config_file_uses_defaults() {
        let loaded = load_config(&["--config-file=/nonexistent/wade/nope.toml".to_string()]);
        assert!(loaded.show_icons_enabled);
        assert_eq!(loaded.config_file_path.as_deref(), Some("/nonexistent/wade/nope.toml"));
    }

    #[test]
    fn only_the_first_config_file_flag_counts() {
        let path = config_with_path("first.toml").config_file_path.unwrap();
        std::fs::write(&path, "show_hidden_files = true\n").unwrap();
        let loaded = load_config(&[format!("--config-file={path}"), "--config-file=/nonexistent/second.toml".to_string()]);
        assert!(loaded.show_hidden_files);
        let _ = std::fs::remove_file(&path);
    }

    #[test]
    fn disabled_tools_names_and_detail_columns_values() {
        let loaded = load_text("tools", "disabled_tools = pdfinfo, ffprobe, unknown,, \n");
        assert!(!loaded.pdf_metadata_enabled && !loaded.ffprobe_enabled);
        assert!(loaded.pdf_preview_enabled && loaded.markdown_preview_enabled && loaded.mediainfo_enabled);

        let enabled = load_text("detail-true", "size_column_enabled = false\ndetail_columns_enabled = true\n");
        assert!(enabled.size_column_enabled && enabled.date_column_enabled);
        let invalid = load_text("detail-bad", "size_column_enabled = false\ndetail_columns_enabled = bogus\n");
        assert!(invalid.size_column_enabled && invalid.date_column_enabled, "invalid falls back to true");
    }

    #[test]
    fn save_creates_the_directory_and_round_trips_every_non_default_value() {
        // Flip every value through the file format first
        let mut text: String = bool_keys().iter().map(|(key, _, default)| format!("{key} = {}\n", !default)).collect();
        text.push_str("sort_mode = extension\n");
        let flipped = load_text("flipped", &text);

        let dir = std::env::temp_dir().join(format!("wade-config-dir-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        let path = dir.join("nested").join("config.toml");
        save_config(&AppConfig { config_file_path: Some(path.to_string_lossy().into_owned()), ..flipped }).unwrap();
        assert!(path.exists(), "save creates the missing directory");

        let reloaded = load_config(&[format!("--config-file={}", path.display())]);
        for (key, field, default) in bool_keys() {
            assert_eq!(field(&reloaded), !default, "{key} round trip");
        }
        assert_eq!(reloaded.sort_mode, SortMode::Extension);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn image_protocol_parses_round_trips_and_is_saved_only_when_set() {
        use crate::imaging::ImageProtocolSetting as Setting;

        let config = config_with_path("image-protocol.txt");
        let path = config_path(&config);
        let load = |text: &str| {
            std::fs::write(&path, text).unwrap();
            load_config(&[format!("--config-file={}", path.display())]).image_protocol
        };

        assert_eq!(load(""), Setting::Auto);
        assert_eq!(load("image_protocol = sixel\n"), Setting::Sixel);
        assert_eq!(load("image_protocol = Kitty  # comment\n"), Setting::Kitty);
        assert_eq!(load("image_protocol = iterm\n"), Setting::Auto, "unknown values keep the default");

        save_config(&AppConfig { image_protocol: Setting::Kitty, ..config.clone() }).unwrap();
        assert!(std::fs::read_to_string(&path).unwrap().ends_with("mediainfo_enabled = true\nimage_protocol = kitty\n"));
        assert_eq!(load(&std::fs::read_to_string(&path).unwrap()), Setting::Kitty);

        save_config(&config).unwrap();
        assert!(!std::fs::read_to_string(&path).unwrap().contains("image_protocol"), "auto keeps the C# layout");
        assert!(to_json(&config).contains("\"image_protocol\":\"auto\","));
        let _ = std::fs::remove_file(&path);
    }

    #[test]
    fn to_json_lists_every_key_in_order() {
        let config = AppConfig { sort_mode: SortMode::Modified, show_hidden_files: true, start_path: "/x".to_string(), ..AppConfig::default() };
        let json = to_json(&config);
        let mut keys: Vec<&str> = bool_keys().iter().map(|(key, _, _)| *key).collect();
        keys.insert(4, "sort_mode");
        keys.push("image_protocol");
        keys.push("start_path");
        let expected_keys: Vec<String> = keys.iter().map(|key| format!("\"{key}\":")).collect();
        let mut position = 0;
        for key in &expected_keys {
            let found = json[position..].find(key.as_str()).unwrap_or_else(|| panic!("{key} missing or out of order in {json}"));
            position += found + key.len();
        }
        assert!(json.contains("\"show_hidden_files\":true,"));
        assert!(json.contains("\"sort_mode\":\"modified\","));
        assert!(json.ends_with("\"start_path\":\"/x\"}"));
        assert_eq!(json.matches(':').count(), expected_keys.len());
    }

    fn cli(args: &[&str]) -> AppConfig {
        let mut config = AppConfig::default();
        apply_cli_args(&mut config, &args.iter().map(|arg| (*arg).to_string()).collect::<Vec<_>>());
        config
    }

    #[test]
    fn cli_flags_and_positional_start_path() {
        assert!(cli(&["--help"]).show_help);
        assert!(cli(&["-h"]).show_help);
        assert!(cli(&["-h"]).start_path.is_empty(), "-h is not a start path");
        assert!(cli(&["--show-config"]).show_config);
        assert!(cli(&["--version"]).show_version);
        assert_eq!(cli(&["--cwd-file=/tmp/cwd"]).cwd_file_path.as_deref(), Some("/tmp/cwd"));
        assert_eq!(cli(&["/some/path"]).start_path, "/some/path");
        assert_eq!(cli(&["--show-config", "/some/path"]).start_path, "/some/path");
        assert_eq!(cli(&["/first", "/second"]).start_path, "/first");
        assert!(cli(&["--show-config"]).start_path.is_empty(), "main defaults it to the current directory");
    }

    #[test]
    fn start_path_trailing_separators_are_stripped_but_roots_kept() {
        for (input, expected) in [
            ("C:\\foo\\bar\\", "C:\\foo\\bar"),
            ("C:\\foo\\bar/", "C:\\foo\\bar"),
            ("C:\\foo\\bar\\\\", "C:\\foo\\bar"),
            ("C:\\foo\\bar", "C:\\foo\\bar"),
            ("C:\\", "C:\\"),
            ("C:/", "C:\\"),
            ("/", "/"),
            ("C:", "C:"),
        ] {
            assert_eq!(normalize_start_path(input), expected, "{input}");
        }
    }

    #[test]
    fn start_path_tilde_expands_to_home() {
        let Some(home) = std::env::var_os("USERPROFILE").or_else(|| std::env::var_os("HOME")) else { return };
        let home = home.to_string_lossy().into_owned();
        for input in ["~", "~/Downloads", "~\\Downloads"] {
            let expanded = normalize_start_path(input);
            assert!(expanded.starts_with(home.trim_end_matches(['/', '\\'])), "{input} -> {expanded}");
            assert!(!expanded.contains('~'), "{input} -> {expanded}");
        }
        assert!(normalize_start_path("~/Downloads").ends_with("Downloads"));
    }
}
