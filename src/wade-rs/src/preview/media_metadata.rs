//! Port of `MediaMetadataProvider`: shells out to `ffprobe` (preferred) or
//! `mediainfo` and maps their JSON to Media File / Video / Audio sections.

use std::collections::HashMap;

use serde_json::value::RawValue;

use super::text_helper::{format_n0, parse_integer};
use super::{MetadataEntry, MetadataProvider, MetadataResult, MetadataSection, PreviewContext};
use crate::input::CancelToken;

const EXTENSIONS: &[&str] = &[
    // Audio
    "mp3", "flac", "wav", "ogg", "aac", "wma", "m4a", "opus", "aiff", // Video
    "mp4", "mkv", "avi", "mov", "wmv", "webm", "flv", "m4v", "ts", "mpg", "mpeg",
];

/// .NET throws `InvalidOperationException` when a JSON property is read
/// from a non-object (and `JsonException` on bad JSON): the result is null.
#[derive(Debug, PartialEq, Eq)]
pub struct JsonError;

type Sections = Result<Option<Vec<MetadataSection>>, JsonError>;

fn extension(path: &str) -> String {
    std::path::Path::new(path)
        .extension()
        .map(|e| e.to_string_lossy().to_ascii_lowercase())
        .unwrap_or_default()
}

fn ffprobe_available() -> bool {
    crate::preview::cli_tool_hints::is_available("ffprobe", Some("-version"), true)
}

fn mediainfo_available() -> bool {
    crate::preview::cli_tool_hints::is_available("mediainfo", Some("--version"), false)
}

pub struct MediaMetadataProvider;

impl MetadataProvider for MediaMetadataProvider {
    fn label(&self) -> &'static str {
        "Media info"
    }

    fn can_provide_metadata(&self, path: &str, context: &PreviewContext) -> bool {
        if !EXTENSIONS.contains(&extension(path).as_str()) {
            return false;
        }

        (context.ffprobe_enabled && ffprobe_available()) || (context.mediainfo_enabled && mediainfo_available())
    }

    fn get_metadata(&self, path: &str, context: &PreviewContext, cancel: &CancelToken) -> Option<MetadataResult> {
        if cancel.is_cancelled() {
            return None;
        }

        let use_ffprobe = context.ffprobe_enabled && ffprobe_available();
        let use_mediainfo = !use_ffprobe && context.mediainfo_enabled && mediainfo_available();

        let json = if use_ffprobe {
            crate::preview::cli_tool_hints::run(
                "ffprobe",
                &["-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", path],
                5000,
                cancel,
            )?
        } else if use_mediainfo {
            crate::preview::cli_tool_hints::run("mediainfo", &["--Output=JSON", path], 5000, cancel)?
        } else {
            return None;
        };

        if cancel.is_cancelled() {
            return None;
        }

        let sections = if use_ffprobe {
            parse_ffprobe_json(&json)
        } else {
            parse_mediainfo_json(&json)
        }
        .ok()??;

        if sections.is_empty() {
            return None;
        }

        Some(MetadataResult {
            sections,
            file_type_label: Some(file_type_label(path)),
        })
    }
}

/// A parsed JSON value kept as its raw text, so numbers print exactly as
/// written (`JsonElement.GetRawText`).
type Value = Box<RawValue>;

fn parse(json: &str) -> Result<Value, JsonError> {
    serde_json::from_str(json).map_err(|_| JsonError)
}

fn first_byte(element: &RawValue) -> Option<u8> {
    element.get().trim_start().bytes().next()
}

/// `JsonElement.TryGetProperty`: throws on a non-object element; a
/// repeated property name yields the last value.
fn property(element: &RawValue, name: &str) -> Result<Option<Value>, JsonError> {
    if first_byte(element) != Some(b'{') {
        return Err(JsonError);
    }

    let mut object: HashMap<String, Value> = serde_json::from_str(element.get()).map_err(|_| JsonError)?;
    Ok(object.remove(name))
}

/// The elements of a JSON array value, or None for any other kind.
fn array(element: &RawValue) -> Result<Option<Vec<Value>>, JsonError> {
    if first_byte(element) != Some(b'[') {
        return Ok(None);
    }

    serde_json::from_str(element.get()).map(Some).map_err(|_| JsonError)
}

/// Port of `GetString`: strings as-is, numbers as their raw JSON text.
fn get_string(element: &RawValue, name: &str) -> Result<Option<String>, JsonError> {
    let Some(value) = property(element, name)? else {
        return Ok(None);
    };

    match first_byte(&value) {
        Some(b'"') => serde_json::from_str(value.get()).map(Some).map_err(|_| JsonError),
        Some(b'-' | b'0'..=b'9') => Ok(Some(value.get().trim().to_string())),
        _ => Ok(None),
    }
}

fn section(header: &str, entries: Vec<MetadataEntry>, sections: &mut Vec<MetadataSection>) {
    if !entries.is_empty() {
        sections.push(MetadataSection {
            header: Some(header.to_string()),
            entries,
        });
    }
}

/// Port of `ParseFfprobeJson`.
pub fn parse_ffprobe_json(json: &str) -> Sections {
    let root = parse(json)?;
    let mut sections = Vec::new();

    if let Some(format) = property(&root, "format")? {
        let mut entries = Vec::new();
        add_json_entry(&mut entries, "Format", &format, "format_long_name", "")?;
        add_duration_entry(&mut entries, &format, "duration")?;
        add_file_size_entry(&mut entries, &format, "size")?;
        add_bit_rate_entry(&mut entries, &format, "bit_rate")?;
        section("Media File", entries, &mut sections);
    }

    if let Some(streams) = property(&root, "streams")?
        && let Some(streams) = array(&streams)?
    {
        for stream in &streams {
            match get_string(stream, "codec_type")?.as_deref() {
                Some("video") => {
                    let mut entries = Vec::new();
                    add_json_entry(&mut entries, "Codec", stream, "codec_long_name", "")?;
                    add_resolution_entry(&mut entries, stream, "width", "height")?;
                    add_frame_rate_entry(&mut entries, stream, "r_frame_rate")?;
                    add_bit_rate_entry(&mut entries, stream, "bit_rate")?;
                    section("Video", entries, &mut sections);
                }
                Some("audio") => {
                    let mut entries = Vec::new();
                    add_json_entry(&mut entries, "Codec", stream, "codec_long_name", "")?;
                    add_channels_entry(&mut entries, stream, "channels", Some("channel_layout"))?;
                    add_sample_rate_entry(&mut entries, stream, "sample_rate")?;
                    add_bit_rate_entry(&mut entries, stream, "bit_rate")?;
                    section("Audio", entries, &mut sections);
                }
                _ => {}
            }
        }
    }

    Ok((!sections.is_empty()).then_some(sections))
}

/// Port of `ParseMediainfoJson`.
pub fn parse_mediainfo_json(json: &str) -> Sections {
    let root = parse(json)?;

    let Some(media) = property(&root, "media")? else {
        return Ok(None);
    };

    let Some(tracks) = property(&media, "track")? else {
        return Ok(None);
    };

    let Some(tracks) = array(&tracks)? else {
        return Ok(None);
    };

    let mut sections = Vec::new();

    for track in &tracks {
        match get_string(track, "@type")?.as_deref() {
            Some("General") => {
                let mut entries = Vec::new();
                add_json_entry(&mut entries, "Format", track, "Format", "")?;
                add_duration_entry(&mut entries, track, "Duration")?;
                add_file_size_entry(&mut entries, track, "FileSize")?;
                add_bit_rate_entry(&mut entries, track, "OverallBitRate")?;
                section("Media File", entries, &mut sections);
            }
            Some("Video") => {
                let mut entries = Vec::new();
                add_json_entry(&mut entries, "Codec", track, "Format", "")?;
                add_resolution_entry(&mut entries, track, "Width", "Height")?;
                add_json_entry(&mut entries, "Frame rate", track, "FrameRate", " fps")?;
                add_bit_rate_entry(&mut entries, track, "BitRate")?;
                section("Video", entries, &mut sections);
            }
            Some("Audio") => {
                let mut entries = Vec::new();
                add_json_entry(&mut entries, "Codec", track, "Format", "")?;
                add_channels_entry(&mut entries, track, "Channels", None)?;
                add_sample_rate_entry(&mut entries, track, "SamplingRate")?;
                add_bit_rate_entry(&mut entries, track, "BitRate")?;
                section("Audio", entries, &mut sections);
            }
            _ => {}
        }
    }

    Ok((!sections.is_empty()).then_some(sections))
}

fn add_json_entry(
    entries: &mut Vec<MetadataEntry>,
    label: &str,
    element: &RawValue,
    name: &str,
    suffix: &str,
) -> Result<(), JsonError> {
    if let Some(value) = get_string(element, name)?.filter(|v| !v.trim().is_empty()) {
        entries.push(MetadataEntry::new(label, &format!("{value}{suffix}")));
    }

    Ok(())
}

/// .NET `double.TryParse(value, InvariantCulture)` (Float | AllowThousands):
/// decimal or exponent forms, thousands commas, and the invariant
/// `NaN`/`Infinity` symbols (Rust's own "inf"/"nan" spellings are not
/// accepted).
fn parse_double(text: &str) -> Option<f64> {
    let trimmed = text.trim().replace(',', "");
    let unsigned = trimmed.trim_start_matches(['+', '-']);

    if unsigned.eq_ignore_ascii_case("nan") {
        return Some(f64::NAN);
    }

    if unsigned.eq_ignore_ascii_case("infinity") {
        return Some(if trimmed.starts_with('-') { f64::NEG_INFINITY } else { f64::INFINITY });
    }

    if !unsigned.bytes().all(|b| b.is_ascii_digit() || matches!(b, b'.' | b'e' | b'E' | b'+' | b'-')) {
        return None;
    }

    trimmed.parse().ok()
}

fn add_duration_entry(entries: &mut Vec<MetadataEntry>, element: &RawValue, name: &str) -> Result<(), JsonError> {
    if let Some(seconds) = get_string(element, name)?.and_then(|v| parse_double(&v))
        && seconds > 0.0
    {
        entries.push(MetadataEntry::new("Duration", &format_duration(seconds)));
    }

    Ok(())
}

fn add_file_size_entry(entries: &mut Vec<MetadataEntry>, element: &RawValue, name: &str) -> Result<(), JsonError> {
    if let Some(bytes) = get_string(element, name)?.and_then(|v| parse_integer::<i64>(&v))
        && bytes > 0
    {
        entries.push(MetadataEntry::new("File size", &format_file_size(bytes)));
    }

    Ok(())
}

fn add_bit_rate_entry(entries: &mut Vec<MetadataEntry>, element: &RawValue, name: &str) -> Result<(), JsonError> {
    if let Some(bps) = get_string(element, name)?.and_then(|v| parse_integer::<i64>(&v))
        && bps > 0
    {
        entries.push(MetadataEntry::new("Bit rate", &format!("{} kbps", format_n0(bps / 1000))));
    }

    Ok(())
}

fn add_resolution_entry(
    entries: &mut Vec<MetadataEntry>,
    element: &RawValue,
    width: &str,
    height: &str,
) -> Result<(), JsonError> {
    if let (Some(w), Some(h)) = (get_string(element, width)?, get_string(element, height)?) {
        entries.push(MetadataEntry::new("Resolution", &format!("{w}\u{00d7}{h}")));
    }

    Ok(())
}

fn add_frame_rate_entry(entries: &mut Vec<MetadataEntry>, element: &RawValue, name: &str) -> Result<(), JsonError> {
    let Some(value) = get_string(element, name)? else {
        return Ok(());
    };

    let parts: Vec<&str> = value.split('/').collect();

    if parts.len() == 2
        && let (Some(num), Some(den)) = (parse_double(parts[0]), parse_double(parts[1]))
        && den > 0.0
    {
        entries.push(MetadataEntry::new("Frame rate", &format!("{:.3} fps", num / den)));
    } else if let Some(fps) = parse_double(&value) {
        entries.push(MetadataEntry::new("Frame rate", &format!("{fps:.3} fps")));
    }

    Ok(())
}

fn add_channels_entry(
    entries: &mut Vec<MetadataEntry>,
    element: &RawValue,
    channels: &str,
    layout: Option<&str>,
) -> Result<(), JsonError> {
    let Some(channels) = get_string(element, channels)? else {
        return Ok(());
    };

    let mut layout = match layout {
        Some(name) => get_string(element, name)?,
        None => None,
    };

    if layout.as_deref().is_none_or(|l| l.trim().is_empty())
        && let Some(count) = parse_integer::<i32>(&channels)
    {
        layout = match count {
            1 => Some("mono".to_string()),
            2 => Some("stereo".to_string()),
            6 => Some("5.1".to_string()),
            8 => Some("7.1".to_string()),
            _ => None,
        };
    }

    let display = match layout {
        Some(layout) => format!("{channels} ({layout})"),
        None => channels,
    };
    entries.push(MetadataEntry::new("Channels", &display));
    Ok(())
}

fn add_sample_rate_entry(entries: &mut Vec<MetadataEntry>, element: &RawValue, name: &str) -> Result<(), JsonError> {
    if let Some(hz) = get_string(element, name)?.and_then(|v| parse_integer::<i32>(&v))
        && hz > 0
    {
        entries.push(MetadataEntry::new("Sample rate", &format!("{} Hz", format_n0(i64::from(hz)))));
    }

    Ok(())
}

/// Port of `FormatDuration`.
#[must_use]
pub fn format_duration(total_seconds: f64) -> String {
    let hours = (total_seconds / 3600.0) as i32;
    let minutes = (total_seconds % 3600.0 / 60.0) as i32;
    let seconds = (total_seconds % 60.0) as i32;

    if hours > 0 {
        format!("{hours}h {minutes:02}m {seconds:02}s")
    } else if minutes > 0 {
        format!("{minutes}m {seconds:02}s")
    } else {
        format!("{seconds}s")
    }
}

/// Port of `FormatFileSize`.
#[must_use]
pub fn format_file_size(bytes: i64) -> String {
    const KB: f64 = 1024.0;
    const MB: f64 = KB * 1024.0;
    const GB: f64 = MB * 1024.0;

    let value = bytes as f64;

    if bytes >= GB as i64 {
        format!("{:.1} GB", value / GB)
    } else if bytes >= MB as i64 {
        format!("{:.1} MB", value / MB)
    } else if bytes >= KB as i64 {
        format!("{:.1} KB", value / KB)
    } else {
        format!("{bytes} B")
    }
}

fn file_type_label(path: &str) -> String {
    let ext = extension(path);

    match ext.as_str() {
        "opus" => "Opus".to_string(),
        "webm" => "WebM".to_string(),
        "ts" => "MPEG-TS".to_string(),
        "mpg" | "mpeg" => "MPEG".to_string(),
        _ => ext.to_ascii_uppercase(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn flatten(sections: &[MetadataSection]) -> Vec<String> {
        sections
            .iter()
            .flat_map(|s| {
                std::iter::once(format!("[{}]", s.header.as_deref().unwrap_or("-")))
                    .chain(s.entries.iter().map(|e| format!("{}: {}", e.label, e.value)))
            })
            .collect()
    }

    #[test]
    fn non_media_extensions_are_rejected() {
        for path in ["readme.txt", "archive.zip", "app.exe", "image.png", "report.docx"] {
            assert!(!MediaMetadataProvider.can_provide_metadata(path, &crate::preview::test_context()), "{path}");
        }
    }

    #[test]
    fn ffprobe_video_and_audio() {
        let json = r#"{"streams":[
            {"codec_type":"video","codec_long_name":"H.264 / AVC","width":1920,"height":1080,"r_frame_rate":"30000/1001","bit_rate":"5000000"},
            {"codec_type":"audio","codec_long_name":"AAC","channels":2,"channel_layout":"stereo","sample_rate":"48000","bit_rate":"128000"}],
            "format":{"format_long_name":"QuickTime / MOV","duration":"3661.5","size":"8912345","bit_rate":"5128000"}}"#;
        assert_eq!(
            flatten(&parse_ffprobe_json(json).unwrap().unwrap()),
            [
                "[Media File]",
                "Format: QuickTime / MOV",
                "Duration: 1h 01m 01s",
                "File size: 8.5 MB",
                "Bit rate: 5,128 kbps",
                "[Video]",
                "Codec: H.264 / AVC",
                "Resolution: 1920\u{d7}1080",
                "Frame rate: 29.970 fps",
                "Bit rate: 5,000 kbps",
                "[Audio]",
                "Codec: AAC",
                "Channels: 2 (stereo)",
                "Sample rate: 48,000 Hz",
                "Bit rate: 128 kbps",
            ]
        );
    }

    #[test]
    fn ffprobe_empty_streams_is_none() {
        assert_eq!(parse_ffprobe_json(r#"{"streams":[]}"#), Ok(None));
    }

    #[test]
    fn non_object_elements_and_bad_json_are_errors() {
        assert_eq!(parse_ffprobe_json("[]"), Err(JsonError));
        assert_eq!(parse_ffprobe_json("{"), Err(JsonError));
        assert_eq!(parse_mediainfo_json(r#"{"media":5}"#), Err(JsonError));
    }

    #[test]
    fn mediainfo_no_media_is_none() {
        assert_eq!(parse_mediainfo_json("{}"), Ok(None));
    }

    #[test]
    fn duration_and_size_format_like_csharp() {
        for (seconds, expected) in
            [(0.0, "0s"), (5.0, "5s"), (65.0, "1m 05s"), (222.5, "3m 42s"), (3661.0, "1h 01m 01s")]
        {
            assert_eq!(format_duration(seconds), expected);
        }

        for (bytes, expected) in [
            (500, "500 B"),
            (1024, "1.0 KB"),
            (1_048_576, "1.0 MB"),
            (8_912_345, "8.5 MB"),
            (1_073_741_824, "1.0 GB"),
        ] {
            assert_eq!(format_file_size(bytes), expected);
        }
    }
}
