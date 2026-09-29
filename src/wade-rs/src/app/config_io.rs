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
            _ => {}
        }
    }

    config
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

    std::fs::write(path, content)
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
}
