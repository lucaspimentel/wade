//! Port of `UnixInputSource` (src/Wade/Terminal/UnixInputSource.cs): reads
//! raw bytes from /dev/tty and decodes them with `VtParser`. C# blocks in
//! `read()` (VMIN=1); this port polls with a 100ms timeout so the pump
//! thread can be cancelled and joined. SIGWINCH sets a flag that becomes a
//! `ResizeEvent` with the current window size.


use std::collections::VecDeque;
use std::sync::atomic::{AtomicBool, Ordering};

use super::vt_parser::VtParser;
use super::{CancelToken, InputEvent, InputSource, ResizeEvent};

const POLL_TIMEOUT_MS: i32 = 100;
/// A lone ESC waits this long for the rest of an escape sequence.
const ESCAPE_WAIT_MS: i32 = 50;

static RESIZED: AtomicBool = AtomicBool::new(false);

extern "C" fn on_sigwinch(_signal: libc::c_int) {
    RESIZED.store(true, Ordering::SeqCst);
}

pub struct UnixInputSource {
    fd: libc::c_int,
    buf: [u8; 64],
    pending: VecDeque<InputEvent>,
    parser: VtParser,
    previous_sigwinch: Option<libc::sigaction>,
}

impl UnixInputSource {
    /// Opens /dev/tty for reading and installs the SIGWINCH handler.
    ///
    /// # Errors
    /// Fails when /dev/tty cannot be opened (no controlling terminal).
    pub fn new() -> std::io::Result<Self> {
        let fd = unsafe { libc::open(c"/dev/tty".as_ptr(), libc::O_RDONLY | libc::O_CLOEXEC) };

        if fd < 0 {
            return Err(std::io::Error::last_os_error());
        }

        let previous_sigwinch = unsafe {
            let mut action: libc::sigaction = std::mem::zeroed();
            action.sa_sigaction = on_sigwinch as *const () as libc::sighandler_t;
            // No SA_RESTART: poll() returns EINTR so the resize is seen at once
            action.sa_flags = 0;
            libc::sigemptyset(&mut action.sa_mask);
            let mut previous: libc::sigaction = std::mem::zeroed();
            (libc::sigaction(libc::SIGWINCH, &action, &mut previous) == 0).then_some(previous)
        };

        Ok(Self { fd, buf: [0; 64], pending: VecDeque::new(), parser: VtParser::new(), previous_sigwinch })
    }

    fn poll(&self, timeout_ms: i32) -> bool {
        let mut pfd = libc::pollfd { fd: self.fd, events: libc::POLLIN, revents: 0 };
        let ready = unsafe { libc::poll(&mut pfd, 1, timeout_ms) };
        ready > 0 && pfd.revents & libc::POLLIN != 0
    }

    fn read_into(&mut self, offset: usize) -> usize {
        let n = unsafe { libc::read(self.fd, self.buf[offset..].as_mut_ptr().cast(), self.buf.len() - offset) };
        usize::try_from(n).unwrap_or(0)
    }
}

impl InputSource for UnixInputSource {
    fn read_next(&mut self, cancel: &CancelToken) -> Option<InputEvent> {
        while !cancel.is_cancelled() {
            if let Some(event) = self.pending.pop_front() {
                return Some(event);
            }

            if RESIZED.swap(false, Ordering::SeqCst) {
                let (width, height) = window_size().unwrap_or((80, 25));
                return Some(InputEvent::Resize(ResizeEvent { width, height }));
            }

            if !self.poll(POLL_TIMEOUT_MS) {
                continue;
            }

            let mut bytes_read = self.read_into(0);

            if bytes_read == 0 {
                std::thread::sleep(std::time::Duration::from_millis(10));
                continue;
            }

            // A lone ESC: wait briefly to tell Escape from a sequence start
            if bytes_read == 1 && self.buf[0] == 0x1B && self.poll(ESCAPE_WAIT_MS) {
                bytes_read += self.read_into(1);
            }

            let events = self.parser.parse(&self.buf[..bytes_read]);
            self.pending.extend(events);
        }

        None
    }

    fn try_take(&mut self) -> Option<InputEvent> {
        self.pending.pop_front()
    }
}

impl Drop for UnixInputSource {
    fn drop(&mut self) {
        unsafe {
            if let Some(previous) = self.previous_sigwinch.take() {
                libc::sigaction(libc::SIGWINCH, &previous, std::ptr::null_mut());
            }

            libc::close(self.fd);
        }
    }
}

/// `Console.WindowWidth`/`WindowHeight`: TIOCGWINSZ on stdout, then on
/// the controlling terminal.
#[must_use]
pub fn window_size() -> Option<(i32, i32)> {
    let query = |fd: libc::c_int| -> Option<(i32, i32)> {
        let mut size: libc::winsize = unsafe { std::mem::zeroed() };
        let ok = unsafe { libc::ioctl(fd, libc::TIOCGWINSZ, &mut size) } == 0;
        (ok && size.ws_col > 0 && size.ws_row > 0).then(|| (i32::from(size.ws_col), i32::from(size.ws_row)))
    };

    query(libc::STDOUT_FILENO).or_else(|| {
        let fd = unsafe { libc::open(c"/dev/tty".as_ptr(), libc::O_RDONLY | libc::O_CLOEXEC) };

        if fd < 0 {
            return None;
        }

        let size = query(fd);
        unsafe { libc::close(fd) };
        size
    })
}
