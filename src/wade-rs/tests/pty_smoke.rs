//! Spike: runs the built `wade` binary in a pseudo-terminal (a Unix PTY, or
//! ConPTY on Windows), sends key bytes and reads the screen back through a
//! VT emulator. Covers what the in-process App tests skip: terminal setup
//! and restore, raw input decoding, resize and the process exit path.

use std::io::{Read, Write};
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
        self.child.try_wait().unwrap().is_none()
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

    wade.send_split(&[(ESC, 15), ("[B", 0)]);
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
