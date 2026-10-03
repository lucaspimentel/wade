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
}

/// Port of `InputReader.MapKey`. `key_char` is the UTF-16 code unit from the
/// C# `KeyEvent.KeyChar`.
#[must_use]
pub fn map_key(key: &KeyEvent) -> AppAction {
    use AppAction as A;
    use ConsoleKey as K;

    let key_char = char::from_u32(u32::from(key.key_char));

    if (key.key == K::P || key.key == K::K) && key.control && !key.shift {
        return A::ShowActionPalette;
    }

    if key_char == Some('?') {
        return A::ShowHelp;
    }

    if key_char == Some('/') {
        return A::Search;
    }

    if key_char == Some('.') {
        return A::ToggleHiddenFiles;
    }

    if key_char == Some('[') {
        return A::ToggleParentPane;
    }

    if key_char == Some(']') {
        return A::TogglePreviewPane;
    }

    if key_char == Some('s') {
        return A::CycleSortMode;
    }

    if key_char == Some('S') {
        return A::ToggleSortDirection;
    }

    if key_char == Some('Q') {
        return A::QuitNoCd;
    }

    if key_char == Some('q') {
        return A::Quit;
    }

    if key_char == Some(',') {
        return A::ShowConfig;
    }

    if key_char == Some('i') {
        return A::ShowProperties;
    }

    if key.key == K::G && key.control && !key.shift {
        return A::GoToPath;
    }

    if key_char == Some('o') {
        return A::OpenExternal;
    }

    if key_char == Some('p') {
        return A::ShowPreviewMenu;
    }

    if key.key == K::Spacebar {
        return A::ToggleMark;
    }

    if key.key == K::R && key.control {
        return A::Refresh;
    }

    if key.key == K::T && key.control {
        return A::OpenTerminal;
    }

    if key.key == K::L && key.control && !key.shift {
        return A::CreateSymlink;
    }

    if key.key == K::F && key.control && !key.shift {
        return A::ShowFileFinder;
    }

    if key_char == Some('c') {
        return A::Copy;
    }

    if key_char == Some('x') {
        return A::Cut;
    }

    if key_char == Some('v') {
        return A::Paste;
    }

    if key_char == Some('y') {
        return A::CopyAbsolutePath;
    }

    if key_char == Some('Y') {
        return A::CopyGitRelativePath;
    }

    if key_char == Some('b') {
        return A::ShowBookmarks;
    }

    if key_char == Some('B') {
        return A::ToggleBookmark;
    }

    if key_char == Some('n') {
        return A::NewFile;
    }

    if key_char == Some('N') {
        return A::NewDirectory;
    }

    if key.key == K::F2 {
        return A::Rename;
    }

    if key.key == K::Delete {
        return if key.shift {
            A::DeletePermanently
        } else {
            A::Delete
        };
    }

    match key.key {
        K::UpArrow | K::K => A::NavigateUp,
        K::DownArrow | K::J => A::NavigateDown,
        K::RightArrow | K::Enter | K::L => A::Open,
        K::LeftArrow | K::Backspace | K::H => A::Back,
        K::Escape => A::Quit,
        K::PageUp => A::PageUp,
        K::PageDown => A::PageDown,
        K::Home => A::Home,
        K::End => A::End,
        K::F5 => A::Refresh,
        _ => A::None,
    }
}


#[cfg(test)]
mod tests {
    use super::*;
    use crate::console_key::ConsoleKey;

    fn make_key(k: ConsoleKey, key_char: u16, shift: bool, alt: bool, control: bool) -> KeyEvent {
        KeyEvent {
            key: k,
            key_char,
            shift,
            alt,
            control,
        }
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
        assert_eq!(map_key(&make_key(ConsoleKey::Escape, 27, false, false, false)), AppAction::Quit);
        assert_eq!(map_key(&make_key(ConsoleKey::PageUp, 0, false, false, false)), AppAction::PageUp);
        assert_eq!(map_key(&make_key(ConsoleKey::PageDown, 0, false, false, false)), AppAction::PageDown);
        assert_eq!(map_key(&make_key(ConsoleKey::Home, 0, false, false, false)), AppAction::Home);
        assert_eq!(map_key(&make_key(ConsoleKey::End, 0, false, false, false)), AppAction::End);
    }

    #[test]
    fn special_chars_map() {
        assert_eq!(map_key(&keyc(ConsoleKey::Oem2 /* needs vk, char matters */, '?', false, false)), AppAction::ShowHelp);
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
        assert_eq!(map_key(&keyc(ConsoleKey::T, 't', false, true)), AppAction::OpenTerminal);
        assert_eq!(map_key(&keyc(ConsoleKey::L, 'l', false, true)), AppAction::CreateSymlink);
        assert_eq!(map_key(&keyc(ConsoleKey::F, 'f', false, true)), AppAction::ShowFileFinder);
    }

    #[test]
    fn space_marks_and_delete_variants() {
        assert_eq!(map_key(&make_key(ConsoleKey::Spacebar, ' ' as u16, false, false, false)), AppAction::ToggleMark);
        assert_eq!(map_key(&make_key(ConsoleKey::Delete, 0, false, false, false)), AppAction::Delete);
        assert_eq!(map_key(&make_key(ConsoleKey::Delete, 0, true, false, false)), AppAction::DeletePermanently);
        assert_eq!(map_key(&make_key(ConsoleKey::F2, 0, false, false, false)), AppAction::Rename);
    }

    #[test]
    fn unrecognized_keys_map_to_none() {
        assert_eq!(map_key(&keyc(ConsoleKey::A, '\0', false, false)), AppAction::None);
        assert_eq!(map_key(&make_key(ConsoleKey::F1, 0, false, false, false)), AppAction::None);
        assert_eq!(map_key(&keyc(ConsoleKey::G, 'g', false, false)), AppAction::None);
    }
}
