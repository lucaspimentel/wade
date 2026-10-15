//! Port of `WadeConfig.Load` / `WadeConfig.Save` (src/Wade/WadeConfig.cs):
//! `~/.config/wade/config.toml`, `key = value` lines with `#` comments; keys
//! match C# exactly.

use crate::preview::PreviewLimits;
use std::path::{Path, PathBuf};

use crate::app::AppConfig;
use crate::fs::directory_contents::SortMode;
use crate::ui::file_icons::IconSet;

fn default_config_path() -> Option<PathBuf> {
    let home = std::env::var_os("USERPROFILE").or_else(|| std::env::var_os("HOME"))?;
    Some(Path::new(&home).join(".config").join("wade").join("config.toml"))
}

/// The path `save_config` writes to: the `--config-file=` override,
/// otherwise the default location (`config.toml` in the current directory
/// when there is no home directory).
#[must_use]
pub fn config_path(config_file: Option<&Path>) -> PathBuf {
    config_file
        .map(Path::to_path_buf)
        .or_else(default_config_path)
        .unwrap_or_else(|| PathBuf::from("config.toml"))
}

/// The command line: `[options] [path]`. Unknown flags are ignored.
#[derive(Debug, Default, PartialEq, Eq)]
pub struct CliArgs {
    /// `--config-file=<path>`; the first one counts.
    pub config_file: Option<PathBuf>,
    /// `--cwd-file=<path>`: receives the final directory; the last one counts.
    pub cwd_file: Option<String>,
    /// The first argument that isn't a flag, normalized.
    pub start_path: Option<String>,
    pub show_config: bool,
    pub show_help: bool,
    pub show_version: bool,
}

#[must_use]
pub fn parse_args(args: &[String]) -> CliArgs {
    let mut cli = CliArgs::default();

    for arg in args {
        if let Some(path) = arg.strip_prefix("--config-file=") {
            cli.config_file.get_or_insert_with(|| PathBuf::from(path));
        } else if let Some(path) = arg.strip_prefix("--cwd-file=") {
            cli.cwd_file = Some(path.to_string());
        } else {
            match arg.as_str() {
                "--show-config" => cli.show_config = true,
                "--help" | "-h" => cli.show_help = true,
                "--version" => cli.show_version = true,
                _ => {}
            }
        }
    }

    cli.start_path = args.iter().find(|arg| !arg.starts_with('-')).map(|start| normalize_start_path(start));
    cli
}

/// Reads `config_file`, or the default location when `None`. A missing or
/// unreadable file gives the defaults.
#[must_use]
pub fn load_config(config_file: Option<&Path>) -> AppConfig {
    let mut config = AppConfig::default();

    let Some(config_path) = config_file.map(Path::to_path_buf).or_else(default_config_path) else {
        return config;
    };
    let Ok(text) = std::fs::read_to_string(&config_path) else {
        return config;
    };

    // Rust-only `icons` replaces the C# `show_icons_enabled`; a config that
    // only has the old key turned off loads as no icons
    let mut icons_key = false;
    let mut legacy_show_icons = true;

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
            "icons" => {
                if let Some(set) = IconSet::parse(value) {
                    config.icon_set = set;
                    icons_key = true;
                }
            }
            "show_icons_enabled" => legacy_show_icons = parse_bool(value, legacy_show_icons),
            "image_previews_enabled" => {
                config.image_previews_enabled = parse_bool(value, config.image_previews_enabled)
            }
            // Rust-only keys: invalid values keep the default, small ones
            // are raised to the minimum
            "csv_preview_enabled" => config.csv_preview_enabled = parse_bool(value, config.csv_preview_enabled),
            "preview_max_lines" => {
                if let Ok(lines) = value.parse::<usize>() {
                    config.preview_max_lines = lines.max(PreviewLimits::MIN_MAX_LINES);
                }
            }
            "preview_max_bytes" => {
                if let Ok(bytes) = value.parse::<u64>() {
                    config.preview_max_bytes = bytes.max(PreviewLimits::MIN_MAX_BYTES);
                }
            }
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
            "confirm_delete_enabled" => {
                config.confirm_delete_enabled = parse_bool(value, config.confirm_delete_enabled)
            }
            "parent_pane_enabled" => config.parent_pane_enabled = parse_bool(value, config.parent_pane_enabled),
            "preview_pane_enabled" => config.preview_pane_enabled = parse_bool(value, config.preview_pane_enabled),
            "size_column_enabled" => config.size_column_enabled = parse_bool(value, config.size_column_enabled),
            "date_column_enabled" => config.date_column_enabled = parse_bool(value, config.date_column_enabled),
            "column_headers_enabled" => {
                config.column_headers_enabled = parse_bool(value, config.column_headers_enabled)
            }
            "copy_symlinks_as_links_enabled" => {
                config.copy_symlinks_as_links_enabled = parse_bool(value, config.copy_symlinks_as_links_enabled);
            }
            "zip_preview_enabled" => config.zip_preview_enabled = parse_bool(value, config.zip_preview_enabled),
            "terminal_title_enabled" => {
                config.terminal_title_enabled = parse_bool(value, config.terminal_title_enabled)
            }
            "git_status_enabled" => config.git_status_enabled = parse_bool(value, config.git_status_enabled),
            "file_metadata_enabled" => config.file_metadata_enabled = parse_bool(value, config.file_metadata_enabled),
            "file_previews_enabled" => config.file_previews_enabled = parse_bool(value, config.file_previews_enabled),
            "archive_metadata_enabled" => {
                config.archive_metadata_enabled = parse_bool(value, config.archive_metadata_enabled)
            }
            "dir_size_ssd_enabled" => config.dir_size_ssd_enabled = parse_bool(value, config.dir_size_ssd_enabled),
            "dir_size_hdd_enabled" => config.dir_size_hdd_enabled = parse_bool(value, config.dir_size_hdd_enabled),
            "dir_size_network_enabled" => {
                config.dir_size_network_enabled = parse_bool(value, config.dir_size_network_enabled)
            }
            "pdf_preview_enabled" => config.pdf_preview_enabled = parse_bool(value, config.pdf_preview_enabled),
            "pdf_metadata_enabled" => config.pdf_metadata_enabled = parse_bool(value, config.pdf_metadata_enabled),
            "markdown_preview_enabled" => {
                config.markdown_preview_enabled = parse_bool(value, config.markdown_preview_enabled)
            }
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

    if !icons_key && !legacy_show_icons {
        config.icon_set = IconSet::None;
    }

    config
}

/// The start-path half of `WadeConfig.Load`: expands a leading `~` to the
/// home directory, then strips trailing separators (both kinds, on every
/// OS) while keeping a root (`/`, `C:\`).
#[must_use]
pub fn normalize_start_path(path: &str) -> String {
    let expanded = if path.starts_with('~') {
        crate::fs::path_completion::expand_tilde(path)
    } else {
        path.to_string()
    };
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
pub fn to_json(config: &AppConfig, start_path: &str) -> String {
    let flag = |value: bool| if value { "true" } else { "false" };
    let sort_mode = config.sort_mode.name();
    let fields = [
        ("icons", ""),
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
        if key == "icons" {
            json.push_str(&format!("\"icons\":\"{}\",", config.icon_set.name()));
        } else if key == "sort_mode" {
            json.push_str(&format!("\"sort_mode\":\"{sort_mode}\","));
        } else {
            json.push_str(&format!("\"{key}\":{value},"));
        }
    }

    // Rust-only key
    json.push_str(&format!("\"image_protocol\":\"{}\",", config.image_protocol.name()));
    json.push_str(&format!("\"csv_preview_enabled\":{},", flag(config.csv_preview_enabled)));
    json.push_str(&format!("\"preview_max_lines\":{},", config.preview_max_lines));
    json.push_str(&format!("\"preview_max_bytes\":{},", config.preview_max_bytes));
    // C# escapes only backslashes in the start path
    json.push_str(&format!("\"start_path\":\"{}\"}}", start_path.replace('\\', "\\\\")));
    json
}

/// Port of `WadeConfig.Save`: all 27 keys in the C# order, except that the
/// Rust-only `icons` takes the place of `show_icons_enabled`.
pub fn save_config(config: &AppConfig, path: &Path) -> std::io::Result<()> {
    if let Some(dir) = path.parent() {
        std::fs::create_dir_all(dir)?;
    }

    let sort_mode = config.sort_mode.name();

    let content = format!(
        "icons = {}\n\
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
        config.icon_set.name(),
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

    if !config.csv_preview_enabled {
        content.push_str("csv_preview_enabled = false\n");
    }

    if config.preview_max_lines != PreviewLimits::DEFAULT_MAX_LINES {
        content.push_str(&format!("preview_max_lines = {}\n", config.preview_max_lines));
    }

    if config.preview_max_bytes != PreviewLimits::DEFAULT_MAX_BYTES {
        content.push_str(&format!("preview_max_bytes = {}\n", config.preview_max_bytes));
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
    match ["0", "1", "2", "3"].iter().position(|number| *number == value) {
        Some(index) => Some(SortMode::ALL[index]),
        None => SortMode::parse(&value.to_ascii_lowercase()),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_config_path(file_name: &str) -> PathBuf {
        std::env::temp_dir().join(format!("wade-config-test-{}-{file_name}", std::process::id()))
    }

    /// The saved file must match the C# `WadeConfig.Save()` layout: 27 keys,
    /// exact order, `key = true|false`, lowercase sort mode (with `icons` in
    /// place of `show_icons_enabled`).
    #[test]
    fn save_writes_exact_csharp_layout() {
        let path = temp_config_path("save.txt");
        save_config(&AppConfig::default(), &path).expect("save");
        let text = std::fs::read_to_string(&path).expect("read back");
        let expected = "\
icons = nerd
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
        let path = temp_config_path("roundtrip.txt");
        let config = AppConfig {
            show_hidden_files: true,
            sort_mode: SortMode::Size,
            dir_size_hdd_enabled: true,
            ..AppConfig::default()
        };
        save_config(&config, &path).expect("save");

        let loaded = load_config(Some(&path));
        assert!(loaded.show_hidden_files);
        assert_eq!(loaded.sort_mode, SortMode::Size);
        assert!(loaded.dir_size_hdd_enabled);
        assert!(loaded.mediainfo_enabled);
    }

    #[test]
    fn cli_args_set_flags_and_first_non_flag_start_path() {
        let parsed = cli(&["--cwd-file=/tmp/out", "-h", "--version", "--show-config", "/start", "/ignored"]);
        assert_eq!(parsed.cwd_file.as_deref(), Some("/tmp/out"));
        assert!(parsed.show_help && parsed.show_version && parsed.show_config);
        assert_eq!(parsed.start_path.as_deref(), Some("/start"));
    }

    #[test]
    fn disabled_tools_and_detail_columns_keys() {
        let path = temp_config_path("legacy.toml");
        std::fs::write(&path, "disabled_tools = pdftopng, markdown_preview ,mediainfo\ndetail_columns_enabled = no\n")
            .unwrap();
        let loaded = load_config(Some(&path));
        assert!(!loaded.pdf_preview_enabled && !loaded.markdown_preview_enabled && !loaded.mediainfo_enabled);
        assert!(loaded.pdf_metadata_enabled && loaded.ffprobe_enabled);
        assert!(!loaded.size_column_enabled && !loaded.date_column_enabled);
        let _ = std::fs::remove_file(path);
    }

    #[test]
    fn to_json_escapes_backslashes_only() {
        let json = to_json(&AppConfig::default(), "C:\\a \"b\"");
        assert!(json.starts_with("{\"icons\":\"nerd\","));
        assert!(json.contains("\"sort_mode\":\"name\","));
        assert!(json.ends_with("\"start_path\":\"C:\\\\a \"b\"\"}"));
    }

    /// Loads `text` as a config file (port of the WadeConfigTests helper).
    fn load_text(name: &str, text: &str) -> AppConfig {
        let path = temp_config_path(name);
        std::fs::write(&path, text).unwrap();
        let loaded = load_config(Some(&path));
        let _ = std::fs::remove_file(&path);
        loaded
    }

    type Field = fn(&AppConfig) -> bool;

    /// Every boolean key, with its field and default.
    fn bool_keys() -> Vec<(&'static str, Field, bool)> {
        vec![
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
        let config = load_config(Some(Path::new("/nonexistent/wade/config.toml")));
        for (key, field, default) in bool_keys() {
            assert_eq!(field(&config), default, "{key} default");
        }
        assert_eq!(config.sort_mode, SortMode::Name);
        assert_eq!(cli(&[]), CliArgs::default());
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
        assert_eq!(
            load_text("sort-bad", "sort_mode = size\nsort_mode = bogus\n").sort_mode,
            SortMode::Size,
            "invalid keeps the value"
        );
    }

    #[test]
    fn comments_blank_malformed_and_unknown_lines_are_ignored() {
        let loaded = load_text(
            "lines",
            "# a comment\n\n   \nshow_hidden_files\n= true\nunknown_key = true\n  show_icons_enabled = false   # inline comment\n  # show_hidden_files = true\n",
        );
        assert_eq!(loaded.icon_set, IconSet::None, "inline comment stripped");
        assert!(!loaded.show_hidden_files, "comment and malformed lines ignored");
    }

    #[test]
    fn missing_config_file_uses_defaults() {
        let loaded = load_config(Some(Path::new("/nonexistent/wade/nope.toml")));
        assert_eq!(loaded.icon_set, IconSet::Nerd);
    }

    #[test]
    fn icons_key_and_the_old_show_icons_key() {
        for set in IconSet::ALL {
            assert_eq!(load_text("icons", &format!("icons = {}\n", set.name())).icon_set, set);
        }
        assert_eq!(load_text("icons-case", "icons = ASCII\n").icon_set, IconSet::Ascii);
        assert_eq!(load_text("icons-bad", "icons = fancy\n").icon_set, IconSet::Nerd, "invalid keeps the default");

        // C# configs: show_icons_enabled = false hides icons unless icons is set
        assert_eq!(load_text("legacy-off", "show_icons_enabled = false\n").icon_set, IconSet::None);
        assert_eq!(load_text("legacy-on", "show_icons_enabled = true\n").icon_set, IconSet::Nerd);
        for text in ["show_icons_enabled = false\nicons = emoji\n", "icons = emoji\nshow_icons_enabled = false\n"] {
            assert_eq!(load_text("legacy-both", text).icon_set, IconSet::Emoji, "{text}");
        }
        assert_eq!(
            load_text("legacy-bad-icons", "show_icons_enabled = false\nicons = fancy\n").icon_set,
            IconSet::None
        );
    }

    #[test]
    fn saved_icon_set_round_trips() {
        for set in IconSet::ALL {
            let path = temp_config_path(&format!("icons-{}.toml", set.name()));
            let config = AppConfig { icon_set: set, ..AppConfig::default() };
            save_config(&config, &path).expect("save");
            let text = std::fs::read_to_string(&path).unwrap();
            assert!(text.starts_with(&format!("icons = {}\n", set.name())), "{text}");
            assert!(!text.contains("show_icons_enabled"));
            assert_eq!(load_config(Some(&path)).icon_set, set);
            let _ = std::fs::remove_file(path);
        }
    }

    #[test]
    fn only_the_first_config_file_flag_counts() {
        let parsed = cli(&["--config-file=/first.toml", "--config-file=/second.toml"]);
        assert_eq!(parsed.config_file.as_deref(), Some(Path::new("/first.toml")));
        assert_eq!(
            cli(&["--cwd-file=/a", "--cwd-file=/b"]).cwd_file.as_deref(),
            Some("/b"),
            "the last cwd file counts"
        );
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
        save_config(&flipped, &path).unwrap();
        assert!(path.exists(), "save creates the missing directory");

        let reloaded = load_config(Some(&path));
        for (key, field, default) in bool_keys() {
            assert_eq!(field(&reloaded), !default, "{key} round trip");
        }
        assert_eq!(reloaded.sort_mode, SortMode::Extension);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn image_protocol_parses_round_trips_and_is_saved_only_when_set() {
        use crate::imaging::ImageProtocolSetting as Setting;

        let config = AppConfig::default();
        let path = temp_config_path("image-protocol.txt");
        let load = |text: &str| {
            std::fs::write(&path, text).unwrap();
            load_config(Some(&path)).image_protocol
        };

        assert_eq!(load(""), Setting::Auto);
        assert_eq!(load("image_protocol = sixel\n"), Setting::Sixel);
        assert_eq!(load("image_protocol = Kitty  # comment\n"), Setting::Kitty);
        assert_eq!(load("image_protocol = iterm\n"), Setting::Iterm);
        assert_eq!(load("image_protocol = iterm2\n"), Setting::Auto, "unknown values keep the default");

        save_config(
            &AppConfig {
                image_protocol: Setting::Kitty,
                ..config.clone()
            },
            &path,
        )
        .unwrap();
        assert!(
            std::fs::read_to_string(&path)
                .unwrap()
                .ends_with("mediainfo_enabled = true\nimage_protocol = kitty\n")
        );
        assert_eq!(load(&std::fs::read_to_string(&path).unwrap()), Setting::Kitty);

        save_config(&config, &path).unwrap();
        assert!(!std::fs::read_to_string(&path).unwrap().contains("image_protocol"), "auto keeps the C# layout");
        assert!(to_json(&config, "").contains("\"image_protocol\":\"auto\","));
        let _ = std::fs::remove_file(&path);
    }

    #[test]
    fn csv_preview_parses_and_is_saved_only_when_off() {
        let config = AppConfig::default();
        let path = temp_config_path("csv-preview.txt");
        let load = |text: &str| {
            std::fs::write(&path, text).unwrap();
            load_config(Some(&path)).csv_preview_enabled
        };

        assert!(load(""));
        assert!(!load("csv_preview_enabled = false\n"));
        assert!(load("csv_preview_enabled = maybe\n"), "invalid values keep the default");

        save_config(
            &AppConfig {
                csv_preview_enabled: false,
                ..config.clone()
            },
            &path,
        )
        .unwrap();
        let saved = std::fs::read_to_string(&path).unwrap();
        assert!(saved.ends_with("mediainfo_enabled = true\ncsv_preview_enabled = false\n"), "{saved}");
        assert!(!load(&saved));

        save_config(&config, &path).unwrap();
        assert!(!std::fs::read_to_string(&path).unwrap().contains("csv_preview"), "on keeps the C# layout");
        assert!(to_json(&config, "").contains("\"csv_preview_enabled\":true,"));
        let _ = std::fs::remove_file(&path);
    }

    #[test]
    fn preview_limits_parse_clamp_and_are_saved_only_when_set() {
        let config = AppConfig::default();
        let path = temp_config_path("preview-limits.txt");
        let load = |text: &str| {
            std::fs::write(&path, text).unwrap();
            let loaded = load_config(Some(&path));
            (loaded.preview_max_lines, loaded.preview_max_bytes)
        };

        assert_eq!(load(""), (10_000, 4 * 1024 * 1024));
        assert_eq!(load("preview_max_lines = 500\npreview_max_bytes = 1048576\n"), (500, 1_048_576));
        assert_eq!(load("preview_max_lines = 5\npreview_max_bytes = 10\n"), (100, 64 * 1024), "raised to the minimum");
        assert_eq!(load("preview_max_lines = lots\npreview_max_bytes = -1\n"), (10_000, 4 * 1024 * 1024));

        save_config(
            &AppConfig {
                preview_max_lines: 500,
                preview_max_bytes: 1_048_576,
                ..config.clone()
            },
            &path,
        )
        .unwrap();
        let saved = std::fs::read_to_string(&path).unwrap();
        assert!(saved.ends_with("mediainfo_enabled = true\npreview_max_lines = 500\npreview_max_bytes = 1048576\n"));
        assert_eq!(load(&saved), (500, 1_048_576));

        save_config(&config, &path).unwrap();
        assert!(!std::fs::read_to_string(&path).unwrap().contains("preview_max"), "defaults keep the C# layout");
        assert!(to_json(&config, "").contains("\"preview_max_lines\":10000,\"preview_max_bytes\":4194304,"));
        let _ = std::fs::remove_file(&path);
    }

    #[test]
    fn to_json_lists_every_key_in_order() {
        let config = AppConfig {
            sort_mode: SortMode::Modified,
            show_hidden_files: true,
            ..AppConfig::default()
        };
        let json = to_json(&config, "/x");
        let mut keys: Vec<&str> = bool_keys().iter().map(|(key, _, _)| *key).collect();
        keys.insert(0, "icons");
        keys.insert(4, "sort_mode");
        keys.push("image_protocol");
        keys.push("csv_preview_enabled");
        keys.push("preview_max_lines");
        keys.push("preview_max_bytes");
        keys.push("start_path");
        let expected_keys: Vec<String> = keys.iter().map(|key| format!("\"{key}\":")).collect();
        let mut position = 0;
        for key in &expected_keys {
            let found = json[position..]
                .find(key.as_str())
                .unwrap_or_else(|| panic!("{key} missing or out of order in {json}"));
            position += found + key.len();
        }
        assert!(json.contains("\"show_hidden_files\":true,"));
        assert!(json.contains("\"sort_mode\":\"modified\","));
        assert!(json.ends_with("\"start_path\":\"/x\"}"));
        assert_eq!(json.matches(':').count(), expected_keys.len());
    }

    fn cli(args: &[&str]) -> CliArgs {
        parse_args(&args.iter().map(|arg| (*arg).to_string()).collect::<Vec<_>>())
    }

    #[test]
    fn cli_flags_and_positional_start_path() {
        assert!(cli(&["--help"]).show_help);
        assert!(cli(&["-h"]).show_help);
        assert!(cli(&["-h"]).start_path.is_none(), "-h is not a start path");
        assert!(cli(&["--show-config"]).show_config);
        assert!(cli(&["--version"]).show_version);
        assert_eq!(cli(&["--cwd-file=/tmp/cwd"]).cwd_file.as_deref(), Some("/tmp/cwd"));
        assert_eq!(cli(&["/some/path"]).start_path.as_deref(), Some("/some/path"));
        assert_eq!(cli(&["--show-config", "/some/path"]).start_path.as_deref(), Some("/some/path"));
        assert_eq!(cli(&["/first", "/second"]).start_path.as_deref(), Some("/first"));
        assert!(cli(&["--show-config"]).start_path.is_none(), "main defaults it to the current directory");
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
        let Some(home) = std::env::var_os("USERPROFILE").or_else(|| std::env::var_os("HOME")) else {
            return;
        };
        let home = home.to_string_lossy().into_owned();
        for input in ["~", "~/Downloads", "~\\Downloads"] {
            let expanded = normalize_start_path(input);
            assert!(expanded.starts_with(home.trim_end_matches(['/', '\\'])), "{input} -> {expanded}");
            assert!(!expanded.contains('~'), "{input} -> {expanded}");
        }
        assert!(normalize_start_path("~/Downloads").ends_with("Downloads"));
    }
}
