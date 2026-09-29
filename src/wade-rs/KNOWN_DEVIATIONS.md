# Known deviations from C# wade

While the port is a strict behavioral clone (see `docs/rust-port-plan.md`),
deliberate deviations are recorded here. The C# retirement trigger requires
this file to be empty or fully accepted.

## Temporary (phase-scoped, expected to be removed by later phases)

- **Unported actions are stubbed with a notification (Phase 3a).** Actions
  whose subsystems land in later phases (git: phase 4; file operations,
  previews: later phases; file finder: phase 5) show a status-bar
  notification ("Not yet ported") instead of performing the action. The
  action palette omits entries whose actions are not yet ported (git,
  preview providers, bookmarks, file operations, file finder, terminal);
  the submenu stack machinery is ported and remaining entries are added in
  their phases.
- **Go-to-path completion is deferred (Phase 3b).** The GoToPath dialog is
  ported (typing, editing, Enter navigation, Escape clear/close, Up-arrow
  parent-directory editing), but Tab/RightArrow suggestion completion and
  the inline ghost suffix depend on `PathCompletion` and land in Phase 3c.
  Tab is consumed as a no-op in the meantime.
- **TextInput dialogs have no completion action yet (Phase 3b).** The
  dialog and key handling are ported, but the Rename/NewFile/NewDirectory
  consumers are file operations from a later phase; Enter with a purpose
  set reports "Not yet ported".
- **Directory timestamps use UTC instead of local time (Phase 3a).**
  `FileSystemEntry.last_modified` converts `SystemTime` without a local
  timezone offset (C# `LastWriteTime` is local). No timezone crate has been
  chosen yet; date display and Modified sort may differ by the UTC offset.
- **Drive media-type detection returns Unknown (Phase 3a).** The C#
  `DriveTypeDetector` (seek-penalty query) port is Phase 9; drive entries
  carry `DriveMediaType.Unknown`, which only affects future SSD/HDD gating.
