//! Port of the Windows half of src/Wade/Terminal/TerminalSetup.cs:
//! VT output enable, raw input mode, UTF-8 code pages, the alternate
//! screen, and WT_SESSION Sixel detection.
//! The Unix termios half is deferred to Phase 9 (see docs/rust-port-plan.md).

#![cfg(windows)]

use windows_sys::Win32::Foundation::HANDLE;
use windows_sys::Win32::System::Console::{
    CONSOLE_MODE, GetConsoleCP, GetConsoleMode, GetConsoleOutputCP, GetConsoleTitleW, GetStdHandle, STD_INPUT_HANDLE,
    STD_OUTPUT_HANDLE, SetConsoleCP, SetConsoleMode, SetConsoleOutputCP, SetConsoleTitleW,
};

/// The console title before wade changed it, NUL-terminated (Rust-only:
/// restored on exit and when the title setting is turned off).
static ORIGINAL_TITLE: std::sync::OnceLock<Vec<u16>> = std::sync::OnceLock::new();

fn save_console_title() {
    let mut buffer = vec![0u16; 1024];
    let len = unsafe { GetConsoleTitleW(buffer.as_mut_ptr(), buffer.len() as u32) } as usize;

    if len > 0 && len < buffer.len() {
        buffer.truncate(len);
        buffer.push(0);
        let _ = ORIGINAL_TITLE.set(buffer);
    }
}

/// Sets the console title back to the one saved at startup, if any.
pub fn restore_console_title() {
    if let Some(title) = ORIGINAL_TITLE.get() {
        unsafe {
            SetConsoleTitleW(title.as_ptr());
        }
    }
}

const CP_UTF8: u32 = 65001;

const ENABLE_VIRTUAL_TERMINAL_PROCESSING: u32 = 0x0004;
const DISABLE_NEWLINE_AUTO_RETURN: u32 = 0x0008;
const ENABLE_LINE_INPUT: u32 = 0x0002;
const ENABLE_ECHO_INPUT: u32 = 0x0004;
const ENABLE_PROCESSED_INPUT: u32 = 0x0001;
const ENABLE_WINDOW_INPUT: u32 = 0x0008;
const ENABLE_MOUSE_INPUT: u32 = 0x0010;
const ENABLE_QUICK_EDIT_MODE: u32 = 0x0040;
const ENABLE_EXTENDED_FLAGS: u32 = 0x0080;

pub use crate::terminal_caps::TerminalCapabilities;

/// Applies Windows console modes. C# does this in the constructor and
/// restores in `Dispose`; the Rust port mirrors that with `new`/`restore`.
pub struct TerminalSetup {
    original_input_mode: CONSOLE_MODE,
    original_output_mode: CONSOLE_MODE,
    original_input_cp: u32,
    original_output_cp: u32,
    restored: bool,
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
        let (original_input_cp, original_output_cp);
        unsafe {
            // C# Console.OutputEncoding/InputEncoding = UTF8
            original_input_cp = GetConsoleCP();
            original_output_cp = GetConsoleOutputCP();
            SetConsoleOutputCP(CP_UTF8);
            SetConsoleCP(CP_UTF8);

            GetConsoleMode(stdout_handle, &mut original_output_mode);
            GetConsoleMode(stdin_handle, &mut original_input_mode);

            // Enable VT processing on output
            SetConsoleMode(
                stdout_handle,
                original_output_mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN,
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
            // Rust-only: WezTerm on Windows shows iTerm2 inline images
            iterm_images: crate::terminal_caps::iterm_from_term_program(std::env::var("TERM_PROGRAM").ok().as_deref()),
            ..TerminalCapabilities::DEFAULT
        };

        save_console_title();
        write_out(&[
            crate::ansi::SAVE_TITLE,
            crate::ansi::ENTER_ALTERNATE_SCREEN,
            crate::ansi::HIDE_CURSOR,
            crate::ansi::CLEAR_SCREEN,
        ]);

        Self {
            original_input_mode,
            original_output_mode,
            original_input_cp,
            original_output_cp,
            restored: false,
            stdin_handle,
            stdout_handle,
            capabilities,
        }
    }

    #[must_use]
    pub const fn capabilities(&self) -> TerminalCapabilities {
        self.capabilities
    }

    /// Port of `Dispose`: leave the alternate screen, then restore the
    /// console modes and code pages. Idempotent.
    pub fn restore(&mut self) {
        if self.restored {
            return;
        }

        self.restored = true;
        write_out(&[
            crate::ansi::RESET_ATTRIBUTES,
            crate::ansi::SHOW_CURSOR,
            crate::ansi::LEAVE_ALTERNATE_SCREEN,
            crate::ansi::EXIT_TITLE,
        ]);
        restore_console_title();

        unsafe {
            SetConsoleMode(self.stdout_handle, self.original_output_mode);
            SetConsoleMode(self.stdin_handle, self.original_input_mode);
            SetConsoleOutputCP(self.original_output_cp);
            SetConsoleCP(self.original_input_cp);
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

fn write_out(parts: &[&str]) {
    use std::io::Write;

    let mut out = std::io::stdout().lock();
    for part in parts {
        let _ = out.write_all(part.as_bytes());
    }
    let _ = out.flush();
}
