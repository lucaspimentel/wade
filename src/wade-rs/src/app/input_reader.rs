//! Port of src/Wade/Terminal/InputReader.cs: AppAction enum and MapKey.

use crate::console_key::ConsoleKey;
use crate::input::KeyEvent;

/// Port of the `AppAction` enum (order matters: it is a C# enum).
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum AppAction {
    None,
    NavigateUp,
    NavigateDown,
    Open,
    Back,
    Quit,
    PageUp,
    PageDown,
    Home,
    End,
    ShowHelp,
    Refresh,
    Search,
    ToggleHiddenFiles,
    ToggleParentPane,
    TogglePreviewPane,
    ToggleMark,
    CycleSortMode,
    ToggleSortDirection,
    /// Rust only: forget the current directory's saved sort.
    ResetDirectorySort,
    GoToPath,
    OpenExternal,
    Rename,
    Delete,
    DeletePermanently,
    Copy,
    Cut,
    Paste,
    NewFile,
    NewDirectory,
    ShowConfig,
    OpenTerminal,
    QuitNoCd,
    ShowProperties,
    ShowActionPalette,
    CopyAbsolutePath,
    CopyGitRelativePath,
    ShowBookmarks,
    ToggleBookmark,
    CreateSymlink,
    ShowFileFinder,
    SelectPreviewProvider,
    ShowPreviewMenu,
    StageFile,
    UnstageFile,
    StageAll,
    UnstageAll,
    GitCommit,
    GitPush,
    GitPushForceWithLease,
    GitPull,
    GitPullRebase,
    GitFetch,
    DownloadCloudFile,
    // Rust only: tabs (#17)
    NewTab,
    NextTab,
    PrevTab,
    /// Tab number 1-9.
    SwitchTab(u8),
    CloseTab,
    /// Rust only: Esc in the file list clears the marks, else the filter,
    /// else hints at `q` (it no longer quits).
    ClearOrQuitHint,
}

/// Port of `InputReader.MapKey`. `key_char` is the UTF-16 code unit from the
/// C# `KeyEvent.KeyChar`.
///
/// Rust only: bare-key bindings match the typed character with no Ctrl or
/// Alt held (`KeyEvent::plain_char`), so Shift+J or Alt+j no longer act as
/// `j`; and the keybinding rule is bare keys in lower/upper-case pairs, with
/// Ctrl only for the jump tools (Ctrl+P/K, Ctrl+F, Ctrl+G) and the browser
/// and shell conventions (Ctrl+T new tab, Ctrl+L/Ctrl+R refresh).
#[must_use]
pub fn map_key(key: &KeyEvent) -> AppAction {
    use AppAction as A;
    use ConsoleKey as K;

    let ctrl = key.control && !key.alt;

    if ctrl && !key.shift {
        match key.key {
            K::P | K::K => return A::ShowActionPalette,
            K::G => return A::GoToPath,
            K::F => return A::ShowFileFinder,
            K::T => return A::NewTab,
            K::L | K::R => return A::Refresh,
            _ => {}
        }
    }

    if let Some(ch) = key.plain_char() {
        match ch {
            '?' => return A::ShowHelp,
            '/' => return A::Search,
            '.' => return A::ToggleHiddenFiles,
            '[' => return A::ToggleParentPane,
            ']' => return A::TogglePreviewPane,
            't' => return A::NewTab,
            'T' => return A::OpenTerminal,
            '}' => return A::NextTab,
            '{' => return A::PrevTab,
            'w' => return A::CloseTab,
            digit @ '1'..='9' => return A::SwitchTab(digit as u8 - b'0'),
            's' => return A::CycleSortMode,
            'S' => return A::ToggleSortDirection,
            'q' => return A::Quit,
            'Q' => return A::QuitNoCd,
            ',' => return A::ShowConfig,
            'i' => return A::ShowProperties,
            'o' => return A::OpenExternal,
            'p' => return A::ShowPreviewMenu,
            'c' => return A::Copy,
            'x' => return A::Cut,
            'v' => return A::Paste,
            'y' => return A::CopyAbsolutePath,
            'Y' => return A::CopyGitRelativePath,
            'b' => return A::ShowBookmarks,
            'B' => return A::ToggleBookmark,
            'n' => return A::NewFile,
            'N' => return A::NewDirectory,
            'd' => return A::Delete,
            'D' => return A::DeletePermanently,
            'r' => return A::Rename,
            'k' => return A::NavigateUp,
            'j' => return A::NavigateDown,
            'l' => return A::Open,
            'h' => return A::Back,
            _ => {}
        }
    }

    if key.key == K::Spacebar {
        return A::ToggleMark;
    }

    if key.key == K::Delete {
        return if key.shift { A::DeletePermanently } else { A::Delete };
    }

    match key.key {
        K::UpArrow => A::NavigateUp,
        K::DownArrow => A::NavigateDown,
        K::RightArrow | K::Enter => A::Open,
        K::LeftArrow | K::Backspace => A::Back,
        K::Escape => A::ClearOrQuitHint,
        K::PageUp => A::PageUp,
        K::PageDown => A::PageDown,
        K::Home => A::Home,
        K::End => A::End,
        K::F2 => A::Rename,
        K::F5 => A::Refresh,
        _ => A::None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::console_key::ConsoleKey;

    fn make_key(k: ConsoleKey, key_char: u16, shift: bool, alt: bool, control: bool) -> KeyEvent {
        KeyEvent { key: k, key_char, shift, alt, control }
    }

    fn keyc(k: ConsoleKey, ch: char, shift: bool, control: bool) -> KeyEvent {
        make_key(k, u16::try_from(u32::from(ch)).unwrap(), shift, false, control)
    }

    #[test]
    fn arrow_keys_map_to_navigation() {
        assert_eq!(map_key(&make_key(ConsoleKey::UpArrow, 0, false, false, false)), AppAction::NavigateUp);
        assert_eq!(map_key(&make_key(ConsoleKey::DownArrow, 0, false, false, false)), AppAction::NavigateDown);
        assert_eq!(map_key(&make_key(ConsoleKey::RightArrow, 0, false, false, false)), AppAction::Open);
        assert_eq!(map_key(&make_key(ConsoleKey::LeftArrow, 0, false, false, false)), AppAction::Back);
    }

    #[test]
    fn vim_keys_map_to_navigation() {
        assert_eq!(map_key(&keyc(ConsoleKey::K, 'k', false, false)), AppAction::NavigateUp);
        assert_eq!(map_key(&keyc(ConsoleKey::J, 'j', false, false)), AppAction::NavigateDown);
        assert_eq!(map_key(&keyc(ConsoleKey::L, 'l', false, false)), AppAction::Open);
        assert_eq!(map_key(&keyc(ConsoleKey::H, 'h', false, false)), AppAction::Back);
    }

    #[test]
    fn navigation_keys_map() {
        assert_eq!(map_key(&make_key(ConsoleKey::Enter, '\r' as u16, false, false, false)), AppAction::Open);
        assert_eq!(map_key(&make_key(ConsoleKey::Backspace, 8, false, false, false)), AppAction::Back);
        assert_eq!(map_key(&make_key(ConsoleKey::Escape, 27, false, false, false)), AppAction::ClearOrQuitHint);
        assert_eq!(map_key(&make_key(ConsoleKey::PageUp, 0, false, false, false)), AppAction::PageUp);
        assert_eq!(map_key(&make_key(ConsoleKey::PageDown, 0, false, false, false)), AppAction::PageDown);
        assert_eq!(map_key(&make_key(ConsoleKey::Home, 0, false, false, false)), AppAction::Home);
        assert_eq!(map_key(&make_key(ConsoleKey::End, 0, false, false, false)), AppAction::End);
    }

    #[test]
    fn special_chars_map() {
        assert_eq!(
            map_key(&keyc(ConsoleKey::Oem2 /* needs vk, char matters */, '?', false, false)),
            AppAction::ShowHelp
        );
        assert_eq!(map_key(&keyc(ConsoleKey::Oem2, '/', false, false)), AppAction::Search);
        assert_eq!(map_key(&keyc(ConsoleKey::OemPeriod, '.', false, false)), AppAction::ToggleHiddenFiles);
        assert_eq!(map_key(&keyc(ConsoleKey::Oem4, '[', false, false)), AppAction::ToggleParentPane);
        assert_eq!(map_key(&keyc(ConsoleKey::Oem6, ']', false, false)), AppAction::TogglePreviewPane);
        assert_eq!(map_key(&keyc(ConsoleKey::S, 's', false, false)), AppAction::CycleSortMode);
        assert_eq!(map_key(&keyc(ConsoleKey::S, 'S', true, false)), AppAction::ToggleSortDirection);
        assert_eq!(map_key(&keyc(ConsoleKey::Q, 'Q', false, false)), AppAction::QuitNoCd);
        assert_eq!(map_key(&keyc(ConsoleKey::Q, 'q', false, false)), AppAction::Quit);
        assert_eq!(map_key(&keyc(ConsoleKey::OemComma, ',', false, false)), AppAction::ShowConfig);
        assert_eq!(map_key(&keyc(ConsoleKey::I, 'i', false, false)), AppAction::ShowProperties);
        assert_eq!(map_key(&keyc(ConsoleKey::O, 'o', false, false)), AppAction::OpenExternal);
        assert_eq!(map_key(&keyc(ConsoleKey::P, 'p', false, false)), AppAction::ShowPreviewMenu);
        assert_eq!(map_key(&keyc(ConsoleKey::C, 'c', false, false)), AppAction::Copy);
        assert_eq!(map_key(&keyc(ConsoleKey::X, 'x', false, false)), AppAction::Cut);
        assert_eq!(map_key(&keyc(ConsoleKey::V, 'v', false, false)), AppAction::Paste);
        assert_eq!(map_key(&keyc(ConsoleKey::Y, 'y', false, false)), AppAction::CopyAbsolutePath);
        assert_eq!(map_key(&keyc(ConsoleKey::Y, 'Y', true, false)), AppAction::CopyGitRelativePath);
        assert_eq!(map_key(&keyc(ConsoleKey::B, 'b', false, false)), AppAction::ShowBookmarks);
        assert_eq!(map_key(&keyc(ConsoleKey::B, 'B', true, false)), AppAction::ToggleBookmark);
        assert_eq!(map_key(&keyc(ConsoleKey::N, 'n', false, false)), AppAction::NewFile);
        assert_eq!(map_key(&keyc(ConsoleKey::N, 'N', true, false)), AppAction::NewDirectory);
    }

    #[test]
    fn ctrl_combinations_map() {
        assert_eq!(map_key(&keyc(ConsoleKey::P, 'p', false, true)), AppAction::ShowActionPalette);
        assert_eq!(map_key(&keyc(ConsoleKey::K, 'k', false, true)), AppAction::ShowActionPalette);
        assert_eq!(map_key(&keyc(ConsoleKey::G, 'g', false, true)), AppAction::GoToPath);
        assert_eq!(map_key(&keyc(ConsoleKey::R, 'r', false, true)), AppAction::Refresh);
        assert_eq!(map_key(&make_key(ConsoleKey::F5, 0, false, false, false)), AppAction::Refresh);
        assert_eq!(map_key(&keyc(ConsoleKey::T, 't', false, true)), AppAction::NewTab);
        assert_eq!(map_key(&keyc(ConsoleKey::L, 'l', false, true)), AppAction::Refresh);
        assert_eq!(map_key(&keyc(ConsoleKey::F, 'f', false, true)), AppAction::ShowFileFinder);
    }

    #[test]
    fn tab_keys_map_without_modifiers() {
        assert_eq!(map_key(&keyc(ConsoleKey::T, 't', false, false)), AppAction::NewTab);
        assert_eq!(map_key(&keyc(ConsoleKey::W, 'w', false, false)), AppAction::CloseTab);
        assert_eq!(map_key(&keyc(ConsoleKey::Oem6, '}', true, false)), AppAction::NextTab);
        assert_eq!(map_key(&keyc(ConsoleKey::Oem4, '{', true, false)), AppAction::PrevTab);
        assert_eq!(map_key(&keyc(ConsoleKey::D1, '1', false, false)), AppAction::SwitchTab(1));
        assert_eq!(map_key(&keyc(ConsoleKey::D9, '9', false, false)), AppAction::SwitchTab(9));
        assert_eq!(map_key(&keyc(ConsoleKey::D0, '0', false, false)), AppAction::None);
        assert_eq!(map_key(&keyc(ConsoleKey::W, 'w', false, true)), AppAction::None, "Ctrl+W is not bound");
    }

    #[test]
    fn space_marks_and_delete_variants() {
        assert_eq!(map_key(&make_key(ConsoleKey::Spacebar, ' ' as u16, false, false, false)), AppAction::ToggleMark);
        assert_eq!(map_key(&make_key(ConsoleKey::Delete, 0, false, false, false)), AppAction::Delete);
        assert_eq!(map_key(&make_key(ConsoleKey::Delete, 0, true, false, false)), AppAction::DeletePermanently);
        assert_eq!(map_key(&make_key(ConsoleKey::F2, 0, false, false, false)), AppAction::Rename);
        assert_eq!(map_key(&keyc(ConsoleKey::D, 'd', false, false)), AppAction::Delete);
        assert_eq!(map_key(&keyc(ConsoleKey::D, 'D', true, false)), AppAction::DeletePermanently);
        assert_eq!(map_key(&keyc(ConsoleKey::R, 'r', false, false)), AppAction::Rename);
        assert_eq!(map_key(&keyc(ConsoleKey::T, 'T', true, false)), AppAction::OpenTerminal);
    }

    #[test]
    fn letters_need_no_ctrl_or_alt_and_case_matters() {
        let alt = |k, c| make_key(k, u16::try_from(u32::from(c)).unwrap(), false, true, false);
        for (k, c) in [(ConsoleKey::H, 'H'), (ConsoleKey::J, 'J'), (ConsoleKey::K, 'K'), (ConsoleKey::L, 'L')] {
            assert_eq!(map_key(&keyc(k, c, true, false)), AppAction::None, "Shift+{c}");
        }
        assert_eq!(map_key(&alt(ConsoleKey::J, 'j')), AppAction::None, "Alt+j");
        assert_eq!(map_key(&alt(ConsoleKey::D, 'd')), AppAction::None, "Alt+d");
        assert_eq!(map_key(&make_key(ConsoleKey::D, 4, false, false, true)), AppAction::None, "Ctrl+D");
        assert_eq!(map_key(&make_key(ConsoleKey::R, 0x12, false, false, true)), AppAction::Refresh, "Ctrl+R");
        assert_eq!(map_key(&make_key(ConsoleKey::L, 0x0C, true, false, true)), AppAction::None, "Ctrl+Shift+L");
        assert_eq!(map_key(&make_key(ConsoleKey::F, 6, true, false, true)), AppAction::None, "Ctrl+Shift+F");
        // AltGr (reported as Ctrl+Alt on Windows) types characters like `[`
        assert_eq!(map_key(&make_key(ConsoleKey::D8, u16::from(b'['), false, true, true)), AppAction::ToggleParentPane);
        assert_eq!(map_key(&make_key(ConsoleKey::D7, u16::from(b'{'), false, true, true)), AppAction::PrevTab);
    }

    #[test]
    fn unrecognized_keys_map_to_none() {
        assert_eq!(map_key(&keyc(ConsoleKey::A, '\0', false, false)), AppAction::None);
        assert_eq!(map_key(&make_key(ConsoleKey::F1, 0, false, false, false)), AppAction::None);
        assert_eq!(map_key(&keyc(ConsoleKey::G, 'g', false, false)), AppAction::None);
    }
}
