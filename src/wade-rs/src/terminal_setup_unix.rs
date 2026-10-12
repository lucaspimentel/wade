//! Port of the unix half of src/Wade/Terminal/TerminalSetup.cs: raw mode
//! on /dev/tty (`cfmakeraw`), the DA1 + cell-size capability query, the
//! alternate screen, SGR mouse reporting and bracketed paste.

use std::io::Write;

pub use crate::terminal_caps::TerminalCapabilities;

/// Written by `restore` after the tty modes: leave the alternate screen,
/// then put the pre-wade title back (Rust-only; C# only clears it).
pub(crate) const RESTORE_SEQUENCE: &[&str] = &[
    crate::ansi::RESET_ATTRIBUTES,
    crate::ansi::SHOW_CURSOR,
    crate::ansi::LEAVE_ALTERNATE_SCREEN,
    crate::ansi::EXIT_TITLE,
];

/// Applies raw mode and the terminal modes; `restore` (or drop) undoes
/// them, as C# `Dispose` does.
pub struct TerminalSetup {
    tty_fd: libc::c_int,
    saved_termios: Option<libc::termios>,
    capabilities: TerminalCapabilities,
    restored: bool,
}

impl TerminalSetup {
    #[must_use]
    pub fn new() -> Self {
        let tty_fd = unsafe { libc::open(c"/dev/tty".as_ptr(), libc::O_RDWR | libc::O_CLOEXEC) };
        let mut saved_termios = None;

        if tty_fd >= 0 {
            unsafe {
                let mut termios: libc::termios = std::mem::zeroed();

                if libc::tcgetattr(tty_fd, &mut termios) == 0 {
                    let mut raw = termios;
                    libc::cfmakeraw(&mut raw);
                    libc::tcsetattr(tty_fd, libc::TCSAFLUSH, &raw);
                    saved_termios = Some(termios);
                }
            }
        }

        // Query capabilities BEFORE entering the alternate screen
        let capabilities = detect_capabilities(tty_fd);

        write_out(&[
            crate::ansi::SAVE_TITLE,
            crate::ansi::ENTER_ALTERNATE_SCREEN,
            crate::ansi::HIDE_CURSOR,
            crate::ansi::CLEAR_SCREEN,
            crate::ansi::ENABLE_MOUSE_REPORTING,
            crate::ansi::ENABLE_SGR_MOUSE_MODE,
            crate::ansi::ENABLE_BRACKETED_PASTE,
        ]);

        Self {
            tty_fd,
            saved_termios,
            capabilities,
            restored: false,
        }
    }

    #[must_use]
    pub const fn capabilities(&self) -> TerminalCapabilities {
        self.capabilities
    }

    /// Port of `Dispose`. Idempotent.
    pub fn restore(&mut self) {
        if self.restored {
            return;
        }

        self.restored = true;
        write_out(&[
            crate::ansi::DISABLE_BRACKETED_PASTE,
            crate::ansi::DISABLE_SGR_MOUSE_MODE,
            crate::ansi::DISABLE_MOUSE_REPORTING,
        ]);

        if let Some(saved) = self.saved_termios
            && self.tty_fd >= 0
        {
            unsafe {
                libc::tcsetattr(self.tty_fd, libc::TCSAFLUSH, &saved);
            }
        }

        write_out(RESTORE_SEQUENCE);

        if self.tty_fd >= 0 {
            unsafe {
                libc::close(self.tty_fd);
            }
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

/// Port of `DetectCapabilitiesUnix`: sends DA1 and `ESC[16t` (plus the
/// kitty queries), then reads
/// replies from the tty until 200ms pass without data.
fn detect_capabilities(tty_fd: libc::c_int) -> TerminalCapabilities {
    if tty_fd < 0 {
        return TerminalCapabilities::DEFAULT;
    }

    // Rust-only: the kitty graphics query and XTVERSION go first; DA1 stays
    // last so terminals that ignore them still answer
    let start = std::time::Instant::now();
    write_out(&["\x1b_Gi=31,s=1,v=1,a=q,t=d,f=24;AAAA\x1b\\\x1b[>0q\x1b[c\x1b[16t"]);
    let mut buf = [0u8; 1024];
    let mut total = 0;

    while total < buf.len() {
        if !crate::input::unix::wait_readable(tty_fd, 200) {
            break;
        }

        let n = unsafe { libc::read(tty_fd, buf[total..].as_mut_ptr().cast(), buf.len() - total) };

        if n <= 0 {
            break;
        }

        total += usize::try_from(n).unwrap_or(0);
    }

    let mut caps = TerminalCapabilities::parse_query_responses(&buf[..total]);
    caps.iterm_images |= crate::terminal_caps::iterm_from_term_program(std::env::var("TERM_PROGRAM").ok().as_deref());

    // Rust-only: inside tmux the replies came from tmux; ask it about the
    // outer terminal and whether image data may pass through
    if std::env::var_os("TMUX").is_some_and(|value| !value.is_empty()) {
        let client = tmux_client().unwrap_or_default();
        caps = caps.inside_tmux(&client);
    }

    crate::terminal_caps::log_detection(&buf[..total], &caps, start.elapsed());
    caps
}

/// Runs `tmux` with `args` and returns its output, or `None` when it fails.
fn tmux(args: &[&str]) -> Option<String> {
    let output = std::process::Command::new("tmux")
        .args(args)
        .stdin(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .output()
        .ok()?;
    output.status.success().then(|| String::from_utf8_lossy(&output.stdout).into_owned())
}

/// The outer terminal and pane as tmux reports them (tmux 3.3+).
fn tmux_client() -> Option<crate::terminal_caps::TmuxClient> {
    let display = tmux(&["display-message", "-p", crate::terminal_caps::TMUX_CLIENT_FORMAT])?;
    let passthrough = tmux(&["show-options", "-Apv", "allow-passthrough"]).unwrap_or_default();
    Some(crate::terminal_caps::TmuxClient::parse(&display, &passthrough))
}

/// The pane's top-left cell on the outer terminal, read again after a
/// resize (splits and zoom move it).
#[must_use]
pub fn tmux_pane_position() -> Option<(i32, i32)> {
    let display = tmux(&["display-message", "-p", "#{pane_top}\t#{pane_left}\t#{status}\t#{status-position}"])?;
    let fields: Vec<&str> = display.trim_end().split('\t').collect();
    let field = |i: usize| fields.get(i).copied().unwrap_or_default();
    Some(crate::terminal_caps::pane_position(field(0), field(1), field(2), field(3)))
}

fn write_out(parts: &[&str]) {
    let mut out = std::io::stdout().lock();

    for part in parts {
        let _ = out.write_all(part.as_bytes());
    }

    let _ = out.flush();
}

#[cfg(test)]
mod tests {
    use super::RESTORE_SEQUENCE;
    use crate::ansi::{EXIT_TITLE, LEAVE_ALTERNATE_SCREEN};

    #[test]
    fn restore_pops_the_saved_title_after_leaving_the_alternate_screen() {
        let leave = RESTORE_SEQUENCE.iter().position(|part| *part == LEAVE_ALTERNATE_SCREEN).unwrap();
        let title = RESTORE_SEQUENCE.iter().position(|part| *part == EXIT_TITLE).unwrap();
        assert!(leave < title);
        assert!(EXIT_TITLE.ends_with("\x1b[23;0t"));
    }
}
