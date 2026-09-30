//! wade: TUI file browser (Rust port of the C# Native AOT tool).

use std::path::Path;

use wade::app::input_reader::AppAction;
use wade::app::App;
use wade::input::{CancelToken, InputSource};

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

    // Pump thread: owns the input source and forwards its events into the
    // app's pipeline, mirroring the C# InputPipeline reader thread.
    #[cfg(windows)]
    let source: Box<dyn InputSource> = Box::new(wade::input::windows::WindowsInputSource::new());
    #[cfg(not(windows))]
    // Unix input lands in Phase 9; stub source that yields no events.
    let source: Box<dyn InputSource> = Box::new(NullInputSource);
    let pump_cancel = cancel.clone();
    let pump_sender = app.pipeline.sender();
    let pump = std::thread::spawn(move || {
        let mut source = source;
        while let Some(event) = source.read_next(&pump_cancel) {
            if pump_sender.send(event).is_err() {
                break; // app loop gone
            }
        }
    });

    let result = app.run(&cancel);
    cancel.cancel();
    let _ = pump.join();

    // C# writes the final directory to stdout for shell integration
    if let Some(cwd) = result {
        println!("{cwd}");
    }

    let _ = AppAction::None; // keep enum import referenced for docs builds
}

/// Placeholder input source for non-Windows platforms (Unix input is
/// implemented in Phase 9). Yields no events, so the app idles instead of
/// reading a terminal.
#[cfg(not(windows))]
struct NullInputSource;

#[cfg(not(windows))]
impl InputSource for NullInputSource {
    fn read_next(&mut self, _cancel: &CancelToken) -> Option<wade::input::InputEvent> {
        None
    }
}
