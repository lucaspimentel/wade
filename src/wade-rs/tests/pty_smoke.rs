//! Spike: runs the built `wade` binary in a pseudo-terminal (a Unix PTY, or
//! ConPTY on Windows), sends key bytes and reads the screen back through a
//! VT emulator. Covers what the in-process App tests skip: terminal setup
//! and restore, raw input decoding, resize and the process exit path.

use std::io::{Read, Write};
#[cfg(unix)]
use std::os::unix::fs::OpenOptionsExt;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use portable_pty::{Child, CommandBuilder, MasterPty, PtySize, native_pty_system};

const COLS: u16 = 100;
const ROWS: u16 = 30;
const TIMEOUT: Duration = Duration::from_secs(15);

const DOWN: &str = "\x1b[B";
const ENTER: &str = "\r";
const ESC: &str = "\x1b";
/// Backspace as most terminals send it (DEL), and as some send it (BS)
const BACKSPACE_DEL: &str = "\x7f";
#[cfg(unix)]
const BACKSPACE_BS: &str = "\x08";
#[cfg(unix)]
/// The start of "Press q to quit": a long temp path in the status bar can
/// cut the rest off.
const QUIT_HINT: &str = "Press q";

fn temp_dir(name: &str) -> PathBuf {
    let dir = std::env::temp_dir().join(format!("wade-pty-{}-{name}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).unwrap();
    dir
}

/// `home` holds the config file; `root` is the listed directory with
/// `alpha/` and `beta/`, each holding a file named after it.
fn fixture(name: &str) -> (PathBuf, PathBuf) {
    let home = temp_dir(name);
    let root = home.join("listed");
    for dir in ["alpha", "beta"] {
        std::fs::create_dir_all(root.join(dir)).unwrap();
        std::fs::write(root.join(dir).join(format!("inside_{dir}.txt")), dir).unwrap();
    }
    std::fs::write(root.join("notes.txt"), "hello from notes\n").unwrap();
    (home, root)
}

/// Inside `beta`: its file is selected, so the right pane's header shows
/// the file name. While `beta` is only selected, that header is "Name"
/// (the status bar path is no help: long temp paths get cut off).
fn beta_opened(screen: &vt100::Screen) -> bool {
    screen.rows(0, COLS).next().is_some_and(|row| row.contains("inside_beta.txt"))
}

/// The status bar's "selected/total" counter, such as "2/3".
fn counter(screen: &vt100::Screen, position: &str) -> bool {
    screen.rows(0, COLS).last().is_some_and(|row| row.trim_end().ends_with(position))
}

/// The control character for Ctrl+`letter`.
#[cfg(unix)]
fn ctrl(letter: char) -> String {
    char::from(letter.to_ascii_uppercase() as u8 - b'@').to_string()
}

struct Session {
    master: Box<dyn MasterPty + Send>,
    writer: Arc<Mutex<Box<dyn Write + Send>>>,
    child: Box<dyn Child + Send + Sync>,
    parser: Arc<Mutex<vt100::Parser>>,
    raw: Arc<Mutex<Vec<u8>>>,
}

impl Session {
    fn start(home: &Path, root: &Path, extra_args: &[String]) -> Self {
        let pair = native_pty_system()
            .openpty(PtySize {
                rows: ROWS,
                cols: COLS,
                pixel_width: 0,
                pixel_height: 0,
            })
            .expect("open pty");

        let mut cmd = CommandBuilder::new(env!("CARGO_BIN_EXE_wade"));
        cmd.arg(format!("--config-file={}", home.join("config.toml").display()));
        for arg in extra_args {
            cmd.arg(arg);
        }
        cmd.arg(root);
        cmd.cwd(root);
        cmd.env("HOME", home);
        cmd.env("USERPROFILE", home);
        cmd.env("TERM", "xterm-256color");

        let child = pair.slave.spawn_command(cmd).expect("spawn wade");
        drop(pair.slave);

        let parser = Arc::new(Mutex::new(vt100::Parser::new(ROWS, COLS, 0)));
        let mut reader = pair.master.try_clone_reader().unwrap();
        let writer = Arc::new(Mutex::new(pair.master.take_writer().unwrap()));
        let raw = Arc::new(Mutex::new(Vec::new()));
        let (sink, replies, log) = (Arc::clone(&parser), Arc::clone(&writer), Arc::clone(&raw));
        std::thread::spawn(move || {
            let mut buf = [0u8; 8192];
            while let Ok(n) = reader.read(&mut buf) {
                if n == 0 {
                    break;
                }
                let chunk = &buf[..n];
                log.lock().unwrap().extend_from_slice(chunk);
                sink.lock().unwrap().process(chunk);
                // ConPTY (started with INHERIT_CURSOR) asks for the cursor
                // position and holds all output until it gets an answer
                if chunk.windows(4).any(|w| w == b"\x1b[6n") {
                    let mut writer = replies.lock().unwrap();
                    let _ = writer.write_all(b"\x1b[1;1R");
                    let _ = writer.flush();
                }
            }
        });

        Self {
            master: pair.master,
            writer,
            child,
            parser,
            raw,
        }
    }

    fn send(&mut self, bytes: &str) {
        let mut writer = self.writer.lock().unwrap();
        writer.write_all(bytes.as_bytes()).unwrap();
        writer.flush().unwrap();
    }

    /// Writes each part, then sleeps for its delay, so the parts arrive in
    /// separate reads.
    fn send_split(&mut self, parts: &[(&str, u64)]) {
        for (bytes, delay_ms) in parts {
            self.send(bytes);
            std::thread::sleep(Duration::from_millis(*delay_ms));
        }
    }

    /// Keys one at a time, as typed. On Windows several printable keys in
    /// one read are taken as a paste (no bracketed paste there), and a paste
    /// does nothing in the file list.
    fn send_keys(&mut self, keys: &str) {
        for c in keys.chars() {
            self.send_split(&[(c.encode_utf8(&mut [0; 4]), 30)]);
        }
    }

    /// Esc on its own: a pause past wade's 50ms wait, so the next key is
    /// not read as Alt+key (ESC then a character is how terminals send Alt).
    /// A slow runner can still read both at once: before the next key,
    /// wait for Esc's effect on screen.
    fn escape(&mut self) {
        self.send_split(&[(ESC, 300)]);
    }

    /// A bracketed paste.
    #[cfg(unix)]
    fn send_paste(&mut self, text: &str) {
        self.send(&format!("\x1b[200~{text}\x1b[201~"));
    }

    /// Waits for `what`, then checks it still holds after a short pause,
    /// so a later queued event that would undo it gets its chance.
    fn settles(&mut self, what: &str, done: impl Fn(&vt100::Screen) -> bool) {
        self.wait_for(what, &done);
        std::thread::sleep(Duration::from_millis(300));
        if !done(self.parser.lock().unwrap().screen()) {
            panic!("{what} did not hold; {}", self.diagnostics());
        }
    }

    #[cfg(unix)]
    fn alive(&mut self) -> bool {
        self.alive_any()
    }

    fn alive_any(&mut self) -> bool {
        self.child.try_wait().unwrap().is_none()
    }

    /// Everything wade has written so far, as text.
    fn raw_text(&self) -> String {
        String::from_utf8_lossy(&self.raw.lock().unwrap()).into_owned()
    }

    /// Waits for `text` in wade's output (escape sequences included).
    fn wait_for_output(&mut self, text: &str) {
        let start = Instant::now();
        while !self.raw_text().contains(text) {
            if start.elapsed() >= TIMEOUT {
                panic!("timed out waiting for output {text:?}; {}", self.diagnostics());
            }
            std::thread::sleep(Duration::from_millis(20));
        }
    }

    /// An SGR mouse press and release at a 0-based cell.
    fn click(&mut self, row: u16, col: u16) {
        self.send(&format!("\x1b[<0;{};{}M\x1b[<0;{};{}m", col + 1, row + 1, col + 1, row + 1));
    }

    /// SGR mouse wheel notches at a 0-based cell, all in one write.
    fn wheel(&mut self, row: u16, col: u16, down: bool, notches: usize) {
        let button = if down { 65 } else { 64 };
        self.send(&format!("\x1b[<{button};{};{}M", col + 1, row + 1).repeat(notches));
    }

    fn screen_text(&self) -> String {
        self.parser.lock().unwrap().screen().contents()
    }

    /// What a failed wait reports: the exit status, the output size, its
    /// tail and the screen.
    fn diagnostics(&mut self) -> String {
        let status = self.child.try_wait();
        let raw = self.raw.lock().unwrap();
        let tail = String::from_utf8_lossy(&raw[raw.len().saturating_sub(600)..]).escape_debug().to_string();
        format!("exit: {status:?}; {} bytes; tail: {tail}\nscreen:\n{}", raw.len(), self.screen_text())
    }

    fn wait_for(&mut self, what: &str, done: impl Fn(&vt100::Screen) -> bool) {
        let start = Instant::now();
        loop {
            if done(self.parser.lock().unwrap().screen()) {
                return;
            }
            if start.elapsed() >= TIMEOUT {
                panic!("timed out waiting for {what}; {}", self.diagnostics());
            }
            std::thread::sleep(Duration::from_millis(20));
        }
    }

    fn wait_for_text(&mut self, text: &str) {
        self.wait_for(text, |screen| screen.contents().contains(text));
    }

    fn resize(&mut self, rows: u16, cols: u16) {
        self.parser.lock().unwrap().screen_mut().set_size(rows, cols);
        self.master
            .resize(PtySize {
                rows,
                cols,
                pixel_width: 0,
                pixel_height: 0,
            })
            .unwrap();
    }

    fn wait_for_exit(&mut self) -> portable_pty::ExitStatus {
        let start = Instant::now();
        loop {
            if let Some(status) = self.child.try_wait().unwrap() {
                return status;
            }
            if start.elapsed() >= TIMEOUT {
                panic!("wade did not exit; {}", self.diagnostics());
            }
            std::thread::sleep(Duration::from_millis(20));
        }
    }
}

impl Drop for Session {
    fn drop(&mut self) {
        let _ = self.child.kill();
    }
}

#[test]
fn starts_in_the_alternate_screen_and_q_restores_the_terminal() {
    let (home, root) = fixture("quit");
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("notes.txt");
    assert!(wade.parser.lock().unwrap().screen().alternate_screen());

    wade.send("q");
    let status = wade.wait_for_exit();
    assert!(status.success(), "exit status {status:?}");
    wade.wait_for("main screen", |screen| !screen.alternate_screen());
    let screen = wade.parser.lock().unwrap().screen().clone();
    assert!(!screen.hide_cursor());
}

#[test]
fn arrow_keys_move_the_selection_and_the_preview_follows() {
    let (home, root) = fixture("arrows");
    let mut wade = Session::start(&home, &root, &[]);
    // alpha is selected first, so its contents show in the right pane
    wade.wait_for_text("inside_alpha.txt");

    wade.send(DOWN);
    wade.wait_for_text("inside_beta.txt");

    wade.send("j");
    wade.wait_for_text("hello from notes");
    wade.send("q");
    wade.wait_for_exit();
}

#[test]
fn enter_opens_a_folder_and_cwd_file_records_it_on_quit() {
    let (home, root) = fixture("cwd");
    let cwd_file = home.join("cwd.txt");
    let mut wade = Session::start(&home, &root, &[format!("--cwd-file={}", cwd_file.display())]);
    wade.wait_for_text("inside_alpha.txt");

    wade.send(DOWN);
    wade.wait_for_text("inside_beta.txt");
    wade.send(ENTER);
    wade.wait_for("beta opened", beta_opened);

    wade.send("q");
    assert!(wade.wait_for_exit().success());
    let recorded = std::fs::read_to_string(&cwd_file).expect("cwd file written");
    assert!(recorded.trim_end().ends_with("beta"), "cwd file holds {recorded:?}");
}

#[test]
fn resizing_the_terminal_redraws_at_the_new_size() {
    let (home, root) = fixture("resize");
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("inside_alpha.txt");

    let wide: u16 = 160;
    wade.resize(ROWS, wide);
    // The status bar fills the bottom row; after the redraw something is
    // drawn past the old width
    wade.wait_for("a redraw past the old width", |screen| {
        (0..ROWS).any(|row| {
            screen.cell(row, wide - 2).is_some_and(|cell| !cell.contents().trim().is_empty())
                || screen.cell(row, COLS + 5).is_some_and(|cell| cell.contents() == "│")
        })
    });
    wade.send("q");
    wade.wait_for_exit();
}

/// Keys that arrive in one read are all handled, in order (the main loop
/// used to keep only the last one, so Enter opened `alpha`).
#[test]
fn keys_sent_together_are_all_handled() {
    let (home, root) = fixture("typeahead");
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("inside_alpha.txt");

    wade.send(&format!("{DOWN}{ENTER}"));
    wade.wait_for("beta opened", beta_opened);
    wade.send("q");
    wade.wait_for_exit();
}

/// Alt+j arrives on Unix as ESC j: one Alt key event, which (like any
/// letter with Alt) does nothing; not Esc followed by j.
#[cfg(unix)]
#[test]
fn alt_letter_is_one_key_not_escape_then_the_letter() {
    let (home, root) = fixture("alt");
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("inside_alpha.txt");

    // Alt+j then a plain j: only the plain j moves
    wade.send("\x1bjj");
    wade.settles("beta selected, no hint", |screen| counter(screen, "2/3") && !screen.contents().contains(QUIT_HINT));
    wade.send("q");
    wade.wait_for_exit();
}

/// A lone Esc waits 50ms for the rest of a sequence: a sequence split by
/// a shorter pause is still one key; after a longer pause Esc stands alone.
#[cfg(unix)]
#[test]
fn escape_alone_and_split_sequences_follow_the_wait() {
    let (home, root) = fixture("esc");
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("inside_alpha.txt");

    // Two writes back to back: usually two reads, joined. A fixed short
    // pause would pass 50ms on a slow runner; the parser's unit tests
    // cover the join without timing
    wade.send_split(&[(ESC, 0), ("[B", 0)]);
    wade.settles("Down from a split sequence", |screen| {
        counter(screen, "2/3") && !screen.contents().contains(QUIT_HINT)
    });

    wade.send_split(&[(ESC, 300), ("j", 0)]);
    wade.settles("Esc, then j", |screen| counter(screen, "3/3") && screen.contents().contains(QUIT_HINT));
    wade.send("q");
    wade.wait_for_exit();
}

/// Starts a rename of `notes.txt` with its name erased; the caller types
/// or pastes the new name and sends Enter.
#[cfg(unix)]
fn start_renaming_notes(wade: &mut Session) {
    wade.wait_for_text("inside_alpha.txt");
    wade.send_keys("jj");
    wade.wait_for_text("hello from notes");
    wade.send("r");
    wade.send(&BACKSPACE_DEL.repeat("notes.txt".len()));
}

/// Waits for `name` to replace `notes.txt` on disk.
fn wait_for_renamed(wade: &mut Session, root: &Path, name: &str) {
    let path = root.join(name);
    wade.wait_for("the file renamed", |_| path.is_file());
    assert!(!root.join("notes.txt").exists());
}

/// Typed text longer than one read (64 bytes) arrives whole, even where a
/// read ends inside a two-byte character. The rename's result on disk shows
/// every character (the input box is narrower than the text).
#[cfg(unix)]
#[test]
fn typed_text_longer_than_a_read_arrives_whole() {
    let (home, root) = fixture("long-typing");
    let mut wade = Session::start(&home, &root, &[]);
    start_renaming_notes(&mut wade);

    // "a" first, so the 64-byte read boundary falls inside an é
    let name = format!("a{}.txt", "é".repeat(40));
    wade.send(&name);
    wade.send(ENTER);
    wait_for_renamed(&mut wade, &root, &name);
    wade.send("q");
    wade.wait_for_exit();
}

/// Arrow keys longer than one read: a sequence cut by the read boundary
/// is joined with the next read, not dropped or typed as letters.
#[cfg(unix)]
#[test]
fn escape_sequences_across_reads_are_joined() {
    let (home, root) = fixture("many-arrows");
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("inside_alpha.txt");

    // 31 Downs over three entries (wrapping) end on the second
    wade.send(&DOWN.repeat(31));
    wade.settles("beta after 31 Downs", |screen| counter(screen, "2/3"));
    wade.send("q");
    wade.wait_for_exit();
}

/// Characters beyond U+FFFF are typed as themselves.
#[cfg(unix)]
#[test]
fn emoji_typed_into_the_filter_arrive_intact() {
    let (home, root) = fixture("emoji");
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("inside_alpha.txt");

    wade.send("/😀x");
    wade.settles("the emoji in the filter", |screen| screen.contents().contains("/😀x"));
    wade.escape();
    wade.wait_for("the filter closed", |screen| !screen.contents().contains("/😀x"));
    wade.send("q");
    wade.wait_for_exit();
}

/// Raw mode turns off Ctrl+C and Ctrl+Z as signals: they reach wade as
/// keys (Ctrl+C clears the filter) and never kill or suspend it.
#[cfg(unix)]
#[test]
fn ctrl_c_and_ctrl_z_are_keys_not_signals() {
    let (home, root) = fixture("ctrl");
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("inside_alpha.txt");

    wade.send("/zzz");
    wade.wait_for("an empty listing", |screen| !screen.contents().contains("notes.txt"));
    wade.send(&ctrl('c'));
    wade.settles("the filter cleared", |screen| screen.contents().contains("notes.txt"));
    assert!(wade.alive());

    wade.escape();
    wade.send(&ctrl('z'));
    wade.send("j");
    wade.settles("still taking keys", |screen| counter(screen, "2/3"));
    assert!(wade.alive());
    wade.send("q");
    assert!(wade.wait_for_exit().success());
}

#[test]
fn filter_narrows_the_list_and_escape_clears_it() {
    let (home, root) = fixture("filter");
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("inside_alpha.txt");

    wade.send_keys("/not");
    wade.wait_for("only notes.txt", |screen| {
        screen.contents().contains("hello from notes") && !screen.contents().contains("alpha")
    });
    wade.send(ENTER);
    wade.settles("the filter applied", |screen| counter(screen, "1/1") && !screen.contents().contains("alpha"));
    wade.escape();
    wade.wait_for("the full list back", |screen| counter(screen, "1/3"));
    wade.send("q");
    wade.wait_for_exit();
}

fn rename_notes_with(backspace: &str, name: &str) {
    let (home, root) = fixture(name);
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("inside_alpha.txt");

    wade.send_keys("jj");
    wade.wait_for_text("hello from notes");
    wade.send("r");
    wade.send(&backspace.repeat("notes.txt".len()));
    wade.send("renamed.txt");
    wade.send(ENTER);
    wait_for_renamed(&mut wade, &root, "renamed.txt");
    wade.wait_for_text("renamed.txt");
    wade.send("q");
    wade.wait_for_exit();
}

#[test]
fn rename_with_backspace_as_del() {
    rename_notes_with(BACKSPACE_DEL, "rename-del");
}

/// Unix only: Windows Terminal sends Backspace as a key event, and ConPTY
/// does not translate 0x08 into one.
#[cfg(unix)]
#[test]
fn rename_with_backspace_as_bs() {
    rename_notes_with(BACKSPACE_BS, "rename-bs");
}

/// Bracketed paste into the filter: the text is inserted, and a newline
/// in it is dropped, never Enter.
#[cfg(unix)]
#[test]
fn bracketed_paste_goes_into_the_filter() {
    let (home, root) = fixture("paste");
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("inside_alpha.txt");

    wade.send("/");
    wade.send_paste("no\nt");
    wade.settles("the pasted filter, still typing", |screen| {
        screen.contents().contains("/not") && !screen.contents().contains("alpha")
    });
    wade.escape();
    wade.wait_for("the filter closed", |screen| !screen.contents().contains("/not"));
    wade.send("q");
    wade.wait_for_exit();
}

/// A paste longer than one read arrives whole.
#[cfg(unix)]
#[test]
fn a_long_bracketed_paste_arrives_whole() {
    let (home, root) = fixture("long-paste");
    let mut wade = Session::start(&home, &root, &[]);
    start_renaming_notes(&mut wade);

    let name = format!("{}.txt", "x".repeat(100));
    wade.send_paste(&name);
    wade.send(ENTER);
    wait_for_renamed(&mut wade, &root, &name);
    wade.send("q");
    wade.wait_for_exit();
}

/// The columns of the pane borders on `row`.
fn borders(screen: &vt100::Screen, row: u16) -> Vec<u16> {
    (0..screen.size().1)
        .filter(|&col| screen.cell(row, col).is_some_and(|cell| cell.contents() == "│"))
        .collect()
}

/// A listed directory with `count` files `file00.txt`, `file01.txt` ...
fn many_files(name: &str, count: usize) -> (PathBuf, PathBuf) {
    let home = temp_dir(name);
    let root = home.join("listed");
    std::fs::create_dir_all(&root).unwrap();
    for i in 0..count {
        std::fs::write(root.join(format!("file{i:02}.txt")), format!("file {i}")).unwrap();
    }
    (home, root)
}

/// Unix only: the PTY's terminal modes, read through its slave device.
#[cfg(unix)]
fn termios_of(slave: &std::fs::File) -> libc::termios {
    use std::os::fd::AsRawFd;
    let mut termios: libc::termios = unsafe { std::mem::zeroed() };
    assert_eq!(unsafe { libc::tcgetattr(slave.as_raw_fd(), &mut termios) }, 0);
    termios
}

/// Raw mode while wade runs; after `q` the terminal modes are back and the
/// exit sequence turns off mouse and paste, shows the cursor, leaves the
/// alternate screen and restores the title, in that order.
#[cfg(unix)]
#[test]
fn quit_restores_raw_mode_and_sends_the_exit_sequence() {
    let (home, root) = fixture("restore");
    let mut wade = Session::start(&home, &root, &[]);
    // Opened before wade exits, so the modes can still be read after
    let tty = wade.master.tty_name().expect("pty name");
    let slave = std::fs::OpenOptions::new().read(true).write(true).custom_flags(libc::O_NOCTTY).open(&tty).unwrap();
    wade.wait_for_text("inside_alpha.txt");
    let running = termios_of(&slave);
    assert_eq!(running.c_lflag & (libc::ICANON | libc::ECHO), 0, "raw mode while running");

    wade.send("q");
    assert!(wade.wait_for_exit().success());
    wade.wait_for("main screen", |screen| !screen.alternate_screen());
    let after = termios_of(&slave);
    assert_eq!(after.c_lflag & (libc::ICANON | libc::ECHO), libc::ICANON | libc::ECHO, "modes restored");

    let raw = wade.raw_text();
    let order = ["\x1b[?2004l", "\x1b[?1006l", "\x1b[?1000l", "\x1b[?25h", "\x1b[?1049l", "\x1b]0;\x07\x1b[23;0t"];
    let positions: Vec<usize> =
        order.iter().map(|part| raw.rfind(part).unwrap_or_else(|| panic!("{part:?} missing"))).collect();
    assert!(positions.is_sorted(), "exit sequence order {positions:?}");
}

/// The title shows the current directory and follows navigation.
#[test]
fn the_title_follows_the_current_directory() {
    let (home, root) = fixture("title");
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("inside_alpha.txt");
    wade.wait_for_output(&format!("\x1b]0;wade - {}\x07", root.display()));

    wade.send(DOWN);
    wade.wait_for_text("inside_beta.txt");
    wade.send(ENTER);
    wade.wait_for("beta opened", beta_opened);
    wade.wait_for_output(&format!("\x1b]0;wade - {}\x07", root.join("beta").display()));
    wade.send("q");
    assert!(wade.wait_for_exit().success());
}

/// With `terminal_title_enabled = false` the title is never set.
#[test]
fn the_title_is_left_alone_when_turned_off() {
    let (home, root) = fixture("no-title");
    std::fs::write(home.join("config.toml"), "terminal_title_enabled = false\n").unwrap();
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("inside_alpha.txt");
    wade.send("q");
    assert!(wade.wait_for_exit().success());
    assert!(!wade.raw_text().contains("wade - "), "no title set");
}

/// SIGTERM and SIGHUP restore the terminal and exit with 128 + the signal.
#[cfg(unix)]
fn signal_restores_the_terminal(signal: libc::c_int, name: &str) {
    let (home, root) = fixture(name);
    let cwd_file = home.join("cwd.txt");
    let mut wade = Session::start(&home, &root, &[format!("--cwd-file={}", cwd_file.display())]);
    wade.wait_for_text("inside_alpha.txt");

    let pid = wade.child.process_id().expect("pid");
    assert_eq!(unsafe { libc::kill(libc::pid_t::try_from(pid).unwrap(), signal) }, 0);
    let status = wade.wait_for_exit();
    assert_eq!(status.exit_code(), 128 + signal.unsigned_abs(), "{status:?}");
    wade.wait_for("main screen", |screen| !screen.alternate_screen() && !screen.hide_cursor());
    assert!(wade.raw_text().contains("\x1b[?1000l"), "mouse reporting off");
    assert!(!cwd_file.exists(), "no cwd file on a signal");
}

#[cfg(unix)]
#[test]
fn sigterm_restores_the_terminal() {
    signal_restores_the_terminal(libc::SIGTERM, "sigterm");
}

#[cfg(unix)]
#[test]
fn sighup_restores_the_terminal() {
    signal_restores_the_terminal(libc::SIGHUP, "sighup");
}

/// Tiny sizes never crash wade, and it redraws when the size comes back.
#[test]
fn tiny_sizes_do_not_crash() {
    let (home, root) = fixture("tiny");
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("inside_alpha.txt");

    for (rows, cols) in [(5, 20), (1, 1), (2, 3), (ROWS, COLS)] {
        wade.resize(rows, cols);
        std::thread::sleep(Duration::from_millis(200));
        assert!(wade.alive_any(), "alive at {rows}x{cols}; {}", wade.diagnostics());
    }
    wade.wait_for_text("inside_alpha.txt");
    wade.send("q");
    assert!(wade.wait_for_exit().success());
}

/// A click in the file list selects the row; the wheel moves the selection
/// one entry per notch, and a burst longer than one read loses no notch.
#[test]
fn mouse_click_and_wheel_move_the_selection() {
    let (home, root) = fixture("mouse");
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("inside_alpha.txt");

    // Rows 0-1 are the column headers; notes.txt is the third entry
    let col = borders(wade.parser.lock().unwrap().screen(), 2)[0] + 4;
    wade.click(4, col);
    wade.wait_for_text("hello from notes");
    wade.click(2, col);
    wade.wait_for_text("inside_alpha.txt");
    wade.send("q");
    wade.wait_for_exit();

    let (home, root) = many_files("wheel", 40);
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("file 0");
    // 30 notches of about 12 bytes: several reads
    wade.wheel(5, col, true, 30);
    wade.settles("30 notches down", |screen| counter(screen, "31/40"));
    wade.wheel(5, col, false, 10);
    wade.settles("10 notches up", |screen| counter(screen, "21/40"));
    wade.send("q");
    wade.wait_for_exit();
}

/// Wide names (CJK, emoji) take two columns in wade and in the terminal,
/// so the pane borders stay in the same columns on every row.
#[test]
fn wide_names_keep_the_pane_borders_aligned() {
    let home = temp_dir("wide");
    let root = home.join("listed");
    std::fs::create_dir_all(root.join("日本語フォルダ")).unwrap();
    std::fs::write(root.join("日本語フォルダ").join("中身.txt"), "x").unwrap();
    std::fs::write(root.join("日本語.txt"), "日本語のテキスト\n").unwrap();
    std::fs::write(root.join("😀.md"), "# 😀 title\n").unwrap();
    std::fs::write(root.join("plain.txt"), "plain\n").unwrap();
    let mut wade = Session::start(&home, &root, &[]);
    wade.wait_for_text("中身.txt");

    for step in ["", "j", "j", "j"] {
        wade.send_keys(step);
        std::thread::sleep(Duration::from_millis(300));
        let screen = wade.parser.lock().unwrap().screen().clone();
        let header = borders(&screen, 0);
        assert_eq!(header.len(), 2, "two borders in the header; {}", wade.screen_text());
        for row in 2..6 {
            assert_eq!(borders(&screen, row), header, "row {row} after {step:?}; {}", wade.screen_text());
        }
    }
    wade.send("q");
    wade.wait_for_exit();
}
