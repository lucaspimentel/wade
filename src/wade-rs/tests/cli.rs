//! Runs the built `wade` binary for the flags that exit before the TUI
//! starts: --version, --help, --show-config and start-path validation.
//! Every run passes --config-file so the user's config is never read.

use std::path::{Path, PathBuf};
use std::process::{Command, Output};

fn temp_dir(name: &str) -> PathBuf {
    let dir = std::env::temp_dir().join(format!("wade-cli-{}-{name}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).unwrap();
    dir
}

fn wade(dir: &Path, args: &[&str]) -> Output {
    let config = dir.join("config.toml");
    Command::new(env!("CARGO_BIN_EXE_wade"))
        .arg(format!("--config-file={}", config.display()))
        .args(args)
        .current_dir(dir)
        .env("HOME", dir)
        .env("USERPROFILE", dir)
        .output()
        .expect("run wade")
}

fn stdout(output: &Output) -> String {
    String::from_utf8_lossy(&output.stdout).into_owned()
}

/// The JSON string value of `key` in `--show-config` output (C# escapes
/// only backslashes).
fn json_string(json: &str, key: &str) -> String {
    let marker = format!("\"{key}\":\"");
    let start = json.find(&marker).unwrap_or_else(|| panic!("{key} missing in {json}")) + marker.len();
    let end = start + json[start..].find("\"}").or_else(|| json[start..].find("\",")).unwrap();
    json[start..end].replace("\\\\", "\\")
}

#[test]
fn version_prints_the_package_version_and_commit() {
    let dir = temp_dir("version");
    let output = wade(&dir, &["--version"]);
    assert!(output.status.success());
    let text = stdout(&output);
    let version = text.trim_end().strip_prefix("wade ").expect("starts with 'wade '");
    let (number, sha) = version.split_once('+').map_or((version, None), |(n, s)| (n, Some(s)));
    assert_eq!(number, env!("CARGO_PKG_VERSION"));
    if let Some(sha) = sha {
        assert_eq!(sha.len(), 40, "full commit sha: {sha}");
        assert!(sha.chars().all(|c| c.is_ascii_hexdigit()));
    }
    let _ = std::fs::remove_dir_all(&dir);
}

#[test]
fn help_prints_usage_and_exits() {
    let dir = temp_dir("help");
    for flag in ["--help", "-h"] {
        let output = wade(&dir, &[flag]);
        assert!(output.status.success(), "{flag}");
        let text = stdout(&output);
        assert!(text.starts_with("wade \u{2014} TUI file browser\n"), "{flag}: {text}");
        assert!(text.contains("Usage: wade [options] [path]"));
        assert!(text.contains("--show-config"));
        assert!(text.contains("Config file: ~/.config/wade/config.toml"));
    }
    let _ = std::fs::remove_dir_all(&dir);
}

#[test]
fn show_config_prints_the_loaded_config_as_json() {
    let dir = temp_dir("show-config");
    std::fs::write(dir.join("config.toml"), "show_hidden_files = true\nsort_mode = size\n").unwrap();
    let output = wade(&dir, &["--show-config"]);
    assert!(output.status.success());
    let json = stdout(&output);
    assert!(json.starts_with('{') && json.trim_end().ends_with('}'), "{json}");
    assert!(json.contains("\"show_hidden_files\":true,"));
    assert!(json.contains("\"sort_mode\":\"size\","));
    assert!(json.contains("\"show_icons_enabled\":true,"), "defaults for keys not in the file");

    // No path argument: the current directory
    let start = PathBuf::from(json_string(&json, "start_path"));
    assert_eq!(start.canonicalize().unwrap(), dir.canonicalize().unwrap());
    let _ = std::fs::remove_dir_all(&dir);
}

#[test]
fn show_config_normalizes_the_start_path() {
    let dir = temp_dir("normalize");
    let sep = std::path::MAIN_SEPARATOR;
    let output = wade(&dir, &["--show-config", &format!("~{sep}sub{sep}{sep}")]);
    let start = json_string(&stdout(&output), "start_path");
    assert_eq!(start, format!("{}{sep}sub", dir.display()), "tilde expanded, trailing separators stripped");
    let _ = std::fs::remove_dir_all(&dir);
}

#[test]
fn missing_start_path_fails_with_a_message() {
    let dir = temp_dir("missing");
    let missing = dir.join("does-not-exist");
    let output = wade(&dir, &[&missing.to_string_lossy()]);
    assert_eq!(output.status.code(), Some(1));
    assert!(output.stdout.is_empty());
    let stderr = String::from_utf8_lossy(&output.stderr);
    assert_eq!(stderr.trim_end(), format!("wade: path does not exist: {}", missing.display()));
    let _ = std::fs::remove_dir_all(&dir);
}
