# Known deviations from C# wade

While the port is a strict behavioral clone (see `docs/rust-port-plan.md`),
deliberate deviations are recorded here. The C# retirement trigger requires
this file to be empty or fully accepted.

## Temporary (phase-scoped, expected to be removed by later phases)

- **Unported actions are stubbed with a notification (Phase 3a).** Actions
  whose subsystems land in later phases (file operations, previews:
  later phases; file finder: phase 5) show a status-bar notification ("Not
  yet ported") instead of performing the action. The action palette omits
  entries whose actions are not yet ported (preview providers, file
  operations, file finder, terminal); the submenu stack machinery is
  ported and remaining entries are added in their phases. Git actions are
  ported (Phase 4b), except "Git: Copy relative path" (Y), which needs the
  OS clipboard and is omitted until Phase 9 alongside the context menu
  Paste/Copy/Cut entries.
- **Config toggles for unported subsystems are inert (Phase 3c).** The
  config dialog ports all 27 settings and persists them, but toggles
  gating subsystems that land in later phases (image/PDF/markdown/archive
  previews, file/archive/pdf/media metadata, git status, directory sizes,
  copy symlinks as links) have no runtime effect until those phases land.
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
