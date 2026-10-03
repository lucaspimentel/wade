//! Every `AppConfig` setting must change what the App shows or does, not
//! just round-trip through the config file. Each setting is applied the way
//! startup applies it (`App::apply_startup_config`) and the way the config
//! dialog applies it (`apply_config_changes`), and toggles that have a key are
//! dispatched too. Probes observe the rendered frame or App state.

use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};

use super::{App, AppAction, AppConfig};
use crate::fs::directory_contents::SortMode;
use crate::fs::DriveMediaType;
use crate::input::InputMode;
use crate::screen::ScreenBuffer;
use crate::ui::config_dialog::ConfigDialogState;

const WIDTH: i32 = 120;
const HEIGHT: i32 = 30;
/// The listed directory's name: unique enough that finding it on screen
/// means the parent pane is drawn.
const CURRENT_DIR: &str = "ZQXcurrent";

static NEXT_ID: AtomicUsize = AtomicUsize::new(0);

/// A fresh fixture tree; returns (parent, listed directory).
fn fixture() -> (PathBuf, PathBuf) {
    let id = NEXT_ID.fetch_add(1, Ordering::SeqCst);
    let parent = std::env::temp_dir().join(format!("wade-settings-{}-{id}", std::process::id()));
    let _ = std::fs::remove_dir_all(&parent);
    let root = parent.join(CURRENT_DIR);
    std::fs::create_dir_all(root.join("sub")).unwrap();
    std::fs::write(root.join("sub").join("inner.txt"), "inner").unwrap();
    std::fs::write(root.join("a.txt"), vec![b'a'; 1234]).unwrap();
    std::fs::write(root.join("b.txt"), "b").unwrap();
    std::fs::write(root.join(".hidden"), "h").unwrap();
    std::fs::write(root.join("readme.md"), "# Title\n").unwrap();
    // An empty zip archive: just the end-of-central-directory record
    let mut zip = b"PK\x05\x06".to_vec();
    zip.extend_from_slice(&[0; 18]);
    std::fs::write(root.join("empty.zip"), zip).unwrap();
    (parent, root)
}

/// An App at `root` with `config` applied as startup applies it, laid out
/// as `run` does. The config file lives in the fixture.
fn app_at(mut config: AppConfig, root: &Path) -> App {
    config.start_path = root.to_string_lossy().into_owned();
    config.config_file_path = Some(root.join("config.toml").to_string_lossy().into_owned());
    let mut app = App::new(config);
    app.apply_startup_config();
    app.set_screen_size(WIDTH, HEIGHT);
    app.layout.calculate(WIDTH, HEIGHT, app.preview_pane_enabled, app.parent_pane_enabled);
    app
}

/// Rendered rows, without the status bar (it shows the current path).
fn frame(app: &mut App) -> Vec<String> {
    let mut buffer = ScreenBuffer::new(WIDTH, HEIGHT);
    app.render(&mut buffer);
    (0..HEIGHT - 1).map(|row| buffer.row_text(row)).collect()
}

fn frame_has(app: &mut App, text: &str) -> bool {
    frame(app).iter().any(|row| row.contains(text))
}

fn row_with(app: &mut App, text: &str) -> String {
    frame(app).into_iter().find(|row| row.contains(text)).unwrap_or_default()
}

fn select(app: &mut App, name: &str) {
    let entries = app.get_visible_entries();
    app.selected_index = entries.iter().position(|e| e.name == name).unwrap_or_else(|| panic!("{name} not listed"));
}

fn visible_names(app: &mut App) -> Vec<String> {
    app.get_visible_entries().into_iter().map(|e| e.name).collect()
}

/// Labels of the preview (`false`) or metadata (`true`) providers for the
/// selected file, after a render picked them.
fn provider_labels(app: &mut App, name: &str, metadata: bool) -> Option<Vec<&'static str>> {
    select(app, name);
    let _ = frame(app);

    if metadata {
        app.preview.applicable_metadata_providers.as_ref().map(|list| list.iter().map(|p| p.label()).collect())
    } else {
        app.preview.applicable_providers.as_ref().map(|list| list.iter().map(|p| p.label()).collect())
    }
}

fn is_icon(ch: char) -> bool {
    matches!(u32::from(ch), 0xE000..=0xF8FF | 0xF0000..=0xFFFFD)
}

/// One setting: how to set it in the config and in the dialog, and a probe
/// that reports whether the App behaves as if it were on.
struct Setting {
    name: &'static str,
    config: fn(&mut AppConfig, bool),
    dialog: fn(&mut ConfigDialogState, bool),
    probe: fn(&mut App, &Path) -> bool,
}

fn settings() -> Vec<Setting> {
    vec![
        Setting {
            name: "parent_pane_enabled",
            config: |c, v| c.parent_pane_enabled = v,
            dialog: |d, v| d.parent_pane = v,
            probe: |app, _| app.layout.left_pane.width > 0 && frame_has(app, CURRENT_DIR),
        },
        Setting {
            name: "preview_pane_enabled",
            config: |c, v| c.preview_pane_enabled = v,
            dialog: |d, v| d.preview_pane = v,
            probe: |app, _| {
                select(app, "sub");
                app.layout.right_pane.width > 0 && frame_has(app, "inner.txt")
            },
        },
        Setting {
            name: "show_hidden_files",
            config: |c, v| c.show_hidden_files = v,
            dialog: |d, v| d.show_hidden_files = v,
            probe: |app, _| visible_names(app).iter().any(|name| name == ".hidden"),
        },
        Setting {
            name: "sort_ascending",
            config: |c, v| c.sort_ascending = v,
            dialog: |d, v| d.sort_ascending = v,
            probe: |app, _| {
                let names = visible_names(app);
                let index = |name: &str| names.iter().position(|n| n == name).unwrap();
                index("a.txt") < index("b.txt")
            },
        },
        Setting {
            name: "show_icons_enabled",
            config: |c, v| c.show_icons_enabled = v,
            dialog: |d, v| d.show_icons = v,
            probe: |app, _| row_with(app, "a.txt").chars().any(is_icon),
        },
        Setting {
            name: "size_column_enabled",
            config: |c, v| c.size_column_enabled = v,
            dialog: |d, v| d.size_column = v,
            probe: |app, _| row_with(app, "a.txt").contains("1.2 KB"),
        },
        Setting {
            name: "date_column_enabled",
            config: |c, v| c.date_column_enabled = v,
            dialog: |d, v| d.date_column = v,
            // Only the center pane's header has a Date column
            probe: |app, _| frame_has(app, "Date"),
        },
        Setting {
            name: "column_headers_enabled",
            config: |c, v| c.column_headers_enabled = v,
            dialog: |d, v| d.column_headers = v,
            probe: |app, _| frame_has(app, "Name"),
        },
        Setting {
            name: "confirm_delete_enabled",
            config: |c, v| c.confirm_delete_enabled = v,
            dialog: |d, v| d.confirm_delete = v,
            probe: |app, _| {
                select(app, "b.txt");
                app.dispatch(AppAction::Delete);
                app.input_mode == InputMode::Confirm
            },
        },
        Setting {
            name: "git_status_enabled",
            config: |c, v| c.git_status_enabled = v,
            dialog: |d, v| d.git_status = v,
            probe: |app, root| {
                std::fs::create_dir_all(root.join(".git")).unwrap();
                app.refresh_git_status();
                app.current_repo_root.is_some()
            },
        },
        Setting {
            name: "terminal_title_enabled",
            config: |c, v| c.terminal_title_enabled = v,
            dialog: |d, v| d.terminal_title = v,
            probe: |app, _| app.terminal_title_sequence().contains("wade - "),
        },
        Setting {
            name: "image_previews_enabled",
            config: |c, v| c.image_previews_enabled = v,
            dialog: |d, v| d.image_previews = v,
            probe: |app, _| {
                app.set_capabilities(crate::terminal_caps::TerminalCapabilities {
                    sixel_supported: true,
                    ..crate::terminal_caps::TerminalCapabilities::DEFAULT
                });
                app.image_previews_effective
            },
        },
        Setting {
            name: "file_previews_enabled",
            config: |c, v| c.file_previews_enabled = v,
            dialog: |d, v| d.file_previews = v,
            probe: |app, _| provider_labels(app, "a.txt", false).is_some_and(|labels| labels.contains(&"Text")),
        },
        Setting {
            name: "file_metadata_enabled",
            config: |c, v| c.file_metadata_enabled = v,
            dialog: |d, v| d.file_metadata = v,
            probe: |app, _| provider_labels(app, "a.txt", true).is_some_and(|labels| labels.contains(&"File info")),
        },
        Setting {
            name: "zip_preview_enabled",
            config: |c, v| c.zip_preview_enabled = v,
            dialog: |d, v| d.zip_preview = v,
            probe: |app, _| {
                provider_labels(app, "empty.zip", false).is_some_and(|labels| labels.contains(&"Archive contents"))
            },
        },
        Setting {
            name: "archive_metadata_enabled",
            config: |c, v| c.archive_metadata_enabled = v,
            dialog: |d, v| d.archive_metadata = v,
            probe: |app, _| {
                provider_labels(app, "empty.zip", true).is_some_and(|labels| labels.contains(&"Archive metadata"))
            },
        },
        Setting {
            name: "markdown_preview_enabled",
            config: |c, v| c.markdown_preview_enabled = v,
            dialog: |d, v| d.markdown_preview = v,
            probe: |app, _| {
                provider_labels(app, "readme.md", false)
                    .is_some_and(|labels| labels.contains(&"Rendered markdown (built-in)"))
            },
        },
        // The PDF and media providers also need external tools, so these
        // check the flag the App hands to the providers
        Setting {
            name: "pdf_preview_enabled",
            config: |c, v| c.pdf_preview_enabled = v,
            dialog: |d, v| d.pdf_preview = v,
            probe: |app, _| app.build_preview_context(40, 20).pdf_preview_enabled,
        },
        Setting {
            name: "pdf_metadata_enabled",
            config: |c, v| c.pdf_metadata_enabled = v,
            dialog: |d, v| d.pdf_metadata = v,
            probe: |app, _| app.build_preview_context(40, 20).pdf_metadata_enabled,
        },
        Setting {
            name: "ffprobe_enabled",
            config: |c, v| c.ffprobe_enabled = v,
            dialog: |d, v| d.ffprobe = v,
            probe: |app, _| app.build_preview_context(40, 20).ffprobe_enabled,
        },
        Setting {
            name: "mediainfo_enabled",
            config: |c, v| c.mediainfo_enabled = v,
            dialog: |d, v| d.mediainfo = v,
            probe: |app, _| app.build_preview_context(40, 20).mediainfo_enabled,
        },
        Setting {
            name: "dir_size_ssd_enabled",
            config: |c, v| c.dir_size_ssd_enabled = v,
            dialog: |d, v| d.dir_size_ssd = v,
            probe: |app, _| inline_sizes_requested(app, DriveMediaType::Ssd),
        },
        Setting {
            name: "dir_size_hdd_enabled",
            config: |c, v| c.dir_size_hdd_enabled = v,
            dialog: |d, v| d.dir_size_hdd = v,
            probe: |app, _| inline_sizes_requested(app, DriveMediaType::Hdd),
        },
        Setting {
            name: "dir_size_network_enabled",
            config: |c, v| c.dir_size_network_enabled = v,
            dialog: |d, v| d.dir_size_network = v,
            probe: |app, _| inline_sizes_requested(app, DriveMediaType::Network),
        },
        #[cfg(unix)]
        Setting {
            name: "copy_symlinks_as_links_enabled",
            config: |c, v| c.copy_symlinks_as_links_enabled = v,
            dialog: |d, v| d.copy_symlinks_as_links = v,
            probe: pasted_symlink_is_link,
        },
        #[cfg(windows)]
        Setting {
            name: "show_system_files",
            config: |c, v| c.show_system_files = v,
            // The dialog nests it under hidden files and saves it only with
            // them (ConfigDialogState.ApplyTo)
            dialog: |d, v| {
                d.show_hidden_files = true;
                d.show_system_files = v;
            },
            probe: |app, root| {
                mark_system(&root.join("a.txt"));
                app.directory_contents.invalidate_all();
                visible_names(app).iter().any(|name| name == "a.txt")
            },
        },
    ]
}

/// Whether navigating here starts the inline directory size scan for a
/// drive of the given type.
fn inline_sizes_requested(app: &mut App, drive: DriveMediaType) -> bool {
    app.detect_drive_media_type = match drive {
        DriveMediaType::Ssd => |_| DriveMediaType::Ssd,
        DriveMediaType::Hdd => |_| DriveMediaType::Hdd,
        _ => |_| DriveMediaType::Network,
    };
    app.refresh_git_status();
    app.inline_dir_sizes.is_some()
}

/// Pastes a symlink into the listed directory and reports whether the copy
/// is still a link.
#[cfg(unix)]
fn pasted_symlink_is_link(app: &mut App, root: &Path) -> bool {
    let source_dir = root.parent().unwrap().join("links");
    std::fs::create_dir_all(&source_dir).unwrap();
    std::os::unix::fs::symlink(root.join("a.txt"), source_dir.join("link.txt")).unwrap();

    app.execute_paste_internal(vec![source_dir.join("link.txt").to_string_lossy().into_owned()], false, false);
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(5);
    loop {
        assert!(std::time::Instant::now() < deadline, "paste did not complete");
        match app.pipeline.try_take() {
            Some(crate::input::InputEvent::FileOperationComplete(event)) => {
                app.handle_file_operation_complete(event);
                break;
            }
            Some(_) => {}
            None => std::thread::sleep(std::time::Duration::from_millis(5)),
        }
    }

    std::fs::symlink_metadata(root.join("link.txt")).unwrap().file_type().is_symlink()
}

#[cfg(windows)]
fn mark_system(path: &Path) {
    use std::os::windows::ffi::OsStrExt;
    use windows_sys::Win32::Storage::FileSystem::{SetFileAttributesW, FILE_ATTRIBUTE_SYSTEM};

    let wide: Vec<u16> = path.as_os_str().encode_wide().chain(std::iter::once(0)).collect();
    assert_ne!(unsafe { SetFileAttributesW(wide.as_ptr(), FILE_ATTRIBUTE_SYSTEM) }, 0);
}

fn default_with(setting: &Setting, value: bool) -> AppConfig {
    let mut config = AppConfig::default();
    (setting.config)(&mut config, value);
    config
}

#[test]
fn every_setting_takes_effect_at_startup() {
    for setting in settings() {
        for value in [true, false] {
            let (parent, root) = fixture();
            let mut app = app_at(default_with(&setting, value), &root);
            assert_eq!((setting.probe)(&mut app, &root), value, "{} = {value} at startup", setting.name);
            let _ = std::fs::remove_dir_all(&parent);
        }
    }
}

#[test]
fn every_setting_takes_effect_from_the_config_dialog() {
    for setting in settings() {
        for value in [true, false] {
            let (parent, root) = fixture();
            let mut app = app_at(default_with(&setting, !value), &root);
            app.show_config_dialog();
            (setting.dialog)(app.modal.config_state.as_mut().unwrap(), value);
            app.apply_config_changes();
            assert_eq!((setting.probe)(&mut app, &root), value, "{} = {value} from the dialog", setting.name);
            let _ = std::fs::remove_dir_all(&parent);
        }
    }
}

#[test]
fn sort_mode_takes_effect_at_startup_and_from_the_dialog() {
    let size_order = |app: &mut App| -> Vec<String> {
        visible_names(app).into_iter().filter(|name| name == "a.txt" || name == "b.txt").collect()
    };

    let (parent, root) = fixture();
    let mut app = app_at(AppConfig { sort_mode: SortMode::Size, sort_ascending: false, ..AppConfig::default() }, &root);
    assert_eq!(size_order(&mut app), ["a.txt", "b.txt"], "largest first at startup");

    let mut app = app_at(AppConfig::default(), &root);
    app.show_config_dialog();
    let state = app.modal.config_state.as_mut().unwrap();
    state.sort_mode = SortMode::Size;
    state.sort_ascending = false;
    app.apply_config_changes();
    assert_eq!(size_order(&mut app), ["a.txt", "b.txt"], "largest first from the dialog");

    let mut app = app_at(AppConfig { sort_mode: SortMode::Size, ..AppConfig::default() }, &root);
    assert_eq!(size_order(&mut app), ["b.txt", "a.txt"], "smallest first");
    let _ = std::fs::remove_dir_all(&parent);
}

#[test]
fn toggle_keys_flip_panes_and_hidden_files() {
    let toggles: [(&str, AppAction); 3] = [
        ("parent_pane_enabled", AppAction::ToggleParentPane),
        ("preview_pane_enabled", AppAction::TogglePreviewPane),
        ("show_hidden_files", AppAction::ToggleHiddenFiles),
    ];
    let all = settings();

    for (name, action) in toggles {
        let setting = all.iter().find(|s| s.name == name).unwrap();
        let (parent, root) = fixture();
        let mut app = app_at(AppConfig::default(), &root);
        let initial = (setting.probe)(&mut app, &root);

        app.dispatch(action);
        assert_eq!((setting.probe)(&mut app, &root), !initial, "{name} after one toggle");
        app.dispatch(action);
        assert_eq!((setting.probe)(&mut app, &root), initial, "{name} after two toggles");
        let _ = std::fs::remove_dir_all(&parent);
    }
}
