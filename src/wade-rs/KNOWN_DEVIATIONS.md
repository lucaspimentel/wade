# Known deviations from C# wade

While the port is a strict behavioral clone (see `docs/rust-port-plan.md`),
deliberate deviations are recorded here. The C# retirement trigger requires
this file to be empty or fully accepted.

## Temporary (phase-scoped, expected to be removed by later phases)

- **Unported actions are stubbed with a notification (Phase 3a).** Actions
  whose subsystems land in later phases (git: 3a+ phase 4; previews: phase 7;
  file operations: later; dialogs/palette/bookmarks/search: 3b/3c) show a
  status-bar notification ("Not yet ported") instead of performing the
  action. The C# retirement trigger requires this section to be gone.
- **Directory timestamps use UTC instead of local time (Phase 3a).**
  `FileSystemEntry.last_modified` converts `SystemTime` without a local
  timezone offset (C# `LastWriteTime` is local). No timezone crate has been
  chosen yet; date display and Modified sort may differ by the UTC offset.
- **Drive media-type detection returns Unknown (Phase 3a).** The C#
  `DriveTypeDetector` (seek-penalty query) port is Phase 9; drive entries
  carry `DriveMediaType.Unknown`, which only affects future SSD/HDD gating.
