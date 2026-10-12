//! Rust only: SIGTERM and SIGHUP end wade the way `q` does, so the terminal
//! leaves raw mode and the alternate screen (C# and the default action
//! kill the process and leave the terminal unusable). The handler only
//! records the signal; a watcher thread cancels the main loop, which then
//! restores the terminal as on a normal exit.

use std::sync::atomic::{AtomicI32, Ordering};
use std::time::Duration;

use crate::input::CancelToken;

static RECEIVED: AtomicI32 = AtomicI32::new(0);

extern "C" fn on_signal(signal: libc::c_int) {
    RECEIVED.store(signal, Ordering::SeqCst);
}

/// Installs the handlers; `cancel` is cancelled once one of the signals
/// arrives.
pub fn install(cancel: &CancelToken) {
    for signal in [libc::SIGTERM, libc::SIGHUP] {
        unsafe {
            let mut action: libc::sigaction = std::mem::zeroed();
            action.sa_sigaction = on_signal as *const () as libc::sighandler_t;
            action.sa_flags = libc::SA_RESTART;
            libc::sigemptyset(&mut action.sa_mask);
            libc::sigaction(signal, &action, std::ptr::null_mut());
        }
    }

    let cancel = cancel.clone();
    std::thread::spawn(move || {
        while received().is_none() {
            std::thread::sleep(Duration::from_millis(50));
        }

        cancel.cancel();
    });
}

/// The signal that ended wade, if one did.
#[must_use]
pub fn received() -> Option<i32> {
    match RECEIVED.load(Ordering::SeqCst) {
        0 => None,
        signal => Some(signal),
    }
}
