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
