//! Port of the `DriveMediaType` enum from
//! `src/Wade/FileSystem/DriveTypeDetector.cs`. Detection itself
//! (`DriveTypeDetector.Detect`) is a Phase 9 item; until then every drive
//! reports `Unknown`, which disables the inline directory-size loader.

/// Port of `DriveMediaType`.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum DriveMediaType {
    Ssd,
    Hdd,
    Network,
    Removable,
    #[default]
    Unknown,
}
