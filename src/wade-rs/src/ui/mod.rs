//! UI layer: ports of src/Wade/UI/*.cs pieces used by the app spine.

pub mod action_palette;
pub mod config_dialog;
pub mod context_menu;
pub mod dialog_box;
pub mod file_icons;
pub mod help_overlay;
pub mod text_input;
pub mod format_helpers;
pub mod layout;
pub mod notification;
pub mod pane_renderer;
pub mod status_bar;

pub use layout::{Layout, Rect};
pub use notification::{Notification, NotificationKind};


// Shared palette, mirroring PaneRenderer's private constants (used by
// StatusBar as well).

pub(crate) const DIR_COLOR: (u8, u8, u8) = (80, 160, 255);
pub(crate) const FILE_COLOR: (u8, u8, u8) = (200, 200, 200);
pub(crate) const SELECTION_FG: (u8, u8, u8) = (0, 0, 0);
pub(crate) const SELECTION_BG: (u8, u8, u8) = (80, 160, 255);
pub(crate) const BORDER_COLOR: (u8, u8, u8) = (60, 60, 60);
pub(crate) const DETAIL_COLOR: (u8, u8, u8) = (110, 110, 110);
pub(crate) const SYMLINK_COLOR: (u8, u8, u8) = (0, 200, 200);
pub(crate) const BROKEN_SYMLINK_COLOR: (u8, u8, u8) = (200, 60, 60);
pub(crate) const CLOUD_PLACEHOLDER_COLOR: (u8, u8, u8) = (140, 140, 160);

pub(crate) const GIT_MODIFIED: (u8, u8, u8) = (220, 180, 50);
pub(crate) const GIT_STAGED: (u8, u8, u8) = (80, 200, 200);
pub(crate) const GIT_UNTRACKED: (u8, u8, u8) = (80, 200, 80);
pub(crate) const GIT_CONFLICT: (u8, u8, u8) = (220, 80, 80);
pub(crate) const MARKED_BG: (u8, u8, u8) = (60, 60, 0);
