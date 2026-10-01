//! wade: TUI file browser (Rust port of the C# Native AOT tool).

use std::path::Path;

use wade::app::input_reader::AppAction;
use wade::app::App;
use wade::input::CancelToken;

fn main() {
    // C# WadeConfig.Load: config file first, then args
    let args: Vec<String> = std::env::args().skip(1).collect();
    let mut config = wade::app::config_io::load_config(&args);

    // C# arg convention: the first non-flag arg is the start path.
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
    let cancel = CancelToken::new();
    let result = app.run(&cancel);

    // C# writes the final directory to stdout for shell integration
    if let Some(cwd) = result {
        println!("{cwd}");
    }

    let _ = AppAction::None; // keep enum import referenced for docs builds
}
