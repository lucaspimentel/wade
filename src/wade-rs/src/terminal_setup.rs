//! Port of the Windows half of src/Wade/Terminal/TerminalSetup.cs:
//! VT output enable, raw input mode, and WT_SESSION Sixel detection.
//! The Unix termios half is deferred to Phase 9 (see docs/rust-port-plan.md).

#![cfg(windows)]

use windows_sys::Win32::Foundation::HANDLE;
use windows_sys::Win32::System::Console::{
    GetConsoleMode, GetStdHandle, SetConsoleMode, CONSOLE_MODE, STD_INPUT_HANDLE, STD_OUTPUT_HANDLE,
};

const ENABLE_VIRTUAL_TERMINAL_PROCESSING: u32 = 0x0004;
const DISABLE_NEWLINE_AUTO_RETURN: u32 = 0x0008;
const ENABLE_LINE_INPUT: u32 = 0x0002;
const ENABLE_ECHO_INPUT: u32 = 0x0004;
const ENABLE_PROCESSED_INPUT: u32 = 0x0001;
const ENABLE_WINDOW_INPUT: u32 = 0x0008;
const ENABLE_MOUSE_INPUT: u32 = 0x0010;
const ENABLE_QUICK_EDIT_MODE: u32 = 0x0040;
const ENABLE_EXTENDED_FLAGS: u32 = 0x0080;

/// Minimal port of the capability fields the input layer needs. The full
/// `TerminalCapabilities` port (sixel, DA1 query parsing) arrives with the
/// imaging phase.
#[derive(Clone, Copy, Debug)]
pub struct TerminalCapabilities {
    pub sixel_supported: bool,
    pub cell_width: u32,
    pub cell_height: u32,
}

impl TerminalCapabilities {
    #[must_use]
    pub const fn default_caps() -> Self {
        Self {
            sixel_supported: false,
            cell_width: 8,
            cell_height: 16,
        }
    }
}

/// Applies Windows console modes. C# does this in the constructor and
/// restores in `Dispose`; the Rust port mirrors that with `new`/`restore`.
pub struct TerminalSetup {
    original_input_mode: CONSOLE_MODE,
    original_output_mode: CONSOLE_MODE,
    stdin_handle: HANDLE,
    stdout_handle: HANDLE,
    capabilities: TerminalCapabilities,
}

impl TerminalSetup {
    /// # Panics
    /// Panics if a console handle cannot be retrieved or a mode cannot be read.
    #[must_use]
    pub fn new() -> Self {
        let stdout_handle = unsafe { GetStdHandle(STD_OUTPUT_HANDLE) };
        let stdin_handle = unsafe { GetStdHandle(STD_INPUT_HANDLE) };

        let mut original_output_mode: CONSOLE_MODE = 0;
        let mut original_input_mode: CONSOLE_MODE = 0;
        unsafe {
            GetConsoleMode(stdout_handle, &mut original_output_mode);
            GetConsoleMode(stdin_handle, &mut original_input_mode);

            // Enable VT processing on output
            SetConsoleMode(
                stdout_handle,
                original_output_mode
                    | ENABLE_VIRTUAL_TERMINAL_PROCESSING
                    | DISABLE_NEWLINE_AUTO_RETURN,
            );

            // Disable line input and echo for raw mode, but do NOT enable
            // ENABLE_VIRTUAL_TERMINAL_INPUT: input uses ReadConsoleInput, which
            // gives structured key records, not VT sequences.
            let input_mode = (original_input_mode
                & !ENABLE_LINE_INPUT
                & !ENABLE_ECHO_INPUT
                & !ENABLE_PROCESSED_INPUT
                & !ENABLE_QUICK_EDIT_MODE)
                | ENABLE_WINDOW_INPUT
                | ENABLE_MOUSE_INPUT
                | ENABLE_EXTENDED_FLAGS;
            SetConsoleMode(stdin_handle, input_mode);
        }

        // Windows Terminal supports Sixel since v1.22; detect via WT_SESSION.
        // Cell pixel size uses defaults (8x16) since VT input is disabled and
        // cell-size queries never come back through ReadConsoleInput.
        let wt_session = std::env::var_os("WT_SESSION").is_some();
        let capabilities = TerminalCapabilities {
            sixel_supported: wt_session,
            cell_width: 8,
            cell_height: 16,
        };

        Self {
            original_input_mode,
            original_output_mode,
            stdin_handle,
            stdout_handle,
            capabilities,
        }
    }

    #[must_use]
    pub const fn capabilities(&self) -> TerminalCapabilities {
        self.capabilities
    }

    pub fn restore(&mut self) {
        unsafe {
            SetConsoleMode(self.stdout_handle, self.original_output_mode);
            SetConsoleMode(self.stdin_handle, self.original_input_mode);
        }
    }
}

impl Default for TerminalSetup {
    fn default() -> Self {
        Self::new()
    }
}

impl Drop for TerminalSetup {
    fn drop(&mut self) {
        self.restore();
    }
}
