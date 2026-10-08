//! wade: TUI file browser (Rust port of the C# Native AOT tool).
//! Port of src/Wade/Program.cs.

use std::path::Path;
use std::process::ExitCode;

use wade::app::App;
use wade::input::CancelToken;

fn main() -> ExitCode {
    // C# WadeConfig.Load: config file first, then args
    let args: Vec<String> = std::env::args().skip(1).collect();
    let mut config = wade::app::config_io::load_config(&args);
    wade::app::config_io::apply_cli_args(&mut config, &args);

    if config.start_path.is_empty() {
        // C# StartPath defaults to Directory.GetCurrentDirectory()
        config.start_path = std::env::current_dir().map(|p| p.to_string_lossy().into_owned()).unwrap_or_default();
    }

    if config.show_version {
        println!("wade {}", version_string());
        return ExitCode::SUCCESS;
    }

    if config.show_help {
        print_help();
        return ExitCode::SUCCESS;
    }

    if config.show_config {
        println!("{}", wade::app::config_io::to_json(&config));
        return ExitCode::SUCCESS;
    }

    // Validate the start path before entering the TUI
    let full_start_path = wade::app::dialogs::get_full_path(&config.start_path);
    // macOS: the stored spelling, so path-keyed state (bookmarks, saved
    // sorts, git status) matches; unchanged elsewhere
    let stored = wade::fs::file_operations::on_disk_case(&full_start_path);
    if stored != full_start_path {
        config.start_path.clone_from(&stored);
    }
    let full = Path::new(&stored);

    if full.is_file() {
        // Path points to a file: open its parent directory and select the file
        if let Some(parent) = full.parent() {
            config.start_file_name = full.file_name().map(|name| name.to_string_lossy().into_owned());
            config.start_path = parent.to_string_lossy().into_owned();
        }
    } else if !full.is_dir() {
        eprintln!("wade: path does not exist: {}", config.start_path);
        return ExitCode::from(1);
    }

    let cwd_file_path = config.cwd_file_path.clone();
    let final_path = App::new(config).run(&CancelToken::new());

    if let (Some(cwd_file), Some(final_path)) = (cwd_file_path, final_path) {
        // C# silently ignores write failures
        let _ = std::fs::write(cwd_file, final_path);
    }

    ExitCode::SUCCESS
}

/// Port of `PrintVersion`: the informational version, `1.14.0+<commit sha>`.
fn version_string() -> String {
    match option_env!("WADE_COMMIT") {
        Some(sha) => format!("{}+{sha}", env!("CARGO_PKG_VERSION")),
        None => env!("CARGO_PKG_VERSION").to_string(),
    }
}

/// Port of `PrintHelp`.
fn print_help() {
    println!(
        "wade \u{2014} TUI file browser

Usage: wade [options] [path]

Options:
  -h, --help                      Show this help and exit
  --version                       Show version and exit
  --show-config                   Print resolved config as JSON and exit
  --config-file=<path>            Use a custom config file

Keybindings:
  ?                               Show keybindings in-app

Config file: ~/.config/wade/config.toml

  show_icons_enabled = true
  image_previews_enabled = true
  image_protocol = auto           # auto, kitty, iterm, sixel
  show_hidden_files = false
  sort_mode = name                # name, modified, size, extension
  sort_ascending = true
  confirm_delete_enabled = true
  preview_pane_enabled = true
  detail_columns_enabled = true"
    );
}

#[cfg(test)]
mod tests {
    /// The C# assembly version comes from Directory.Build.props; both
    /// binaries must report the same number until the C# code is retired.
    #[test]
    fn cargo_version_matches_directory_build_props() {
        let props = std::fs::read_to_string(concat!(env!("CARGO_MANIFEST_DIR"), "/../../Directory.Build.props"))
            .expect("read Directory.Build.props");
        let start = props.find("<Version>").expect("<Version> element") + "<Version>".len();
        let end = start + props[start..].find("</Version>").expect("</Version>");
        assert_eq!(&props[start..end], env!("CARGO_PKG_VERSION"));
    }
}
