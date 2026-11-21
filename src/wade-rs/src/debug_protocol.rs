//! The `WADE_DEBUG_PROTOCOL` diagnostic lines: what terminal detection found
//! (line 1, written before the alternate screen) and the image-preview
//! decision (line 2, written after the app exits). One stderr line each, and
//! only when the variable is `1`, `true` or `yes`.

use crate::imaging::{ImageProtocol, ImageProtocolSetting};
use crate::terminal_caps::{TerminalCapabilities, TmuxClient};

/// The environment variable that turns the dump on.
const VARIABLE: &str = "WADE_DEBUG_PROTOCOL";

/// The prefix both lines start with.
const PREFIX: &str = "wade-debug: ";

/// The platform name both lines report.
const PLATFORM: &str = if cfg!(windows) { "windows" } else { "unix" };

/// The values that enable the dump, compared case-insensitively.
const TRUTHY: [&str; 3] = ["1", "true", "yes"];

/// Whether this value of `WADE_DEBUG_PROTOCOL` enables the dump. Anything
/// else (unset, empty, `on`, `0`) disables it, silently, and the value is not
/// trimmed.
#[must_use]
fn enabled_in(value: Option<&str>) -> bool {
    value.is_some_and(|value| TRUTHY.iter().any(|truthy| value.eq_ignore_ascii_case(truthy)))
}

/// [`enabled_in`] on this process's environment.
#[must_use]
fn enabled() -> bool {
    enabled_in(std::env::var(VARIABLE).ok().as_deref())
}

/// The capability-query reply bytes, or why there are none.
pub enum RawReplies<'a> {
    /// The bytes the terminal answered with, whole.
    Bytes(&'a [u8]),
    /// Nothing was asked, with the reason.
    Absent(&'static str),
}

/// Line 1's inputs, all of them known where detection happens.
pub struct DetectionInputs<'a> {
    pub capabilities: TerminalCapabilities,
    pub tmux_client: Option<&'a TmuxClient>,
    pub raw: RawReplies<'a>,
}

/// The image-preview decision, read back from `App` for line 2.
#[derive(Clone, Copy, Debug)]
pub struct ImageDecision {
    pub setting: ImageProtocolSetting,
    pub protocol: Option<ImageProtocol>,
    pub effective: bool,
    pub previews_enabled: bool,
}

/// The environment the reported values come from, as name and value pairs, so
/// a test can supply its own instead of mutating the process environment.
struct Env(Vec<(String, String)>);

impl Env {
    /// This process's environment, reduced to the variables the line reports.
    /// The rest is never copied, so a large environment costs nothing.
    #[must_use]
    fn from_process() -> Self {
        let reported =
            |name: &str| ALWAYS_REPORTED.contains(&name) || FAMILIES.iter().any(|prefix| name.starts_with(prefix));
        Self(
            std::env::vars_os()
                .filter_map(|(name, value)| Some((name.into_string().ok()?, value)))
                .filter(|(name, _)| reported(name))
                .map(|(name, value)| (name, value.to_string_lossy().into_owned()))
                .collect(),
        )
    }

    /// The reported value of `name`: `<unset>` when it is not there. A
    /// variable that is set but empty stays empty, so the two differ.
    fn value(&self, name: &str) -> &str {
        self.0
            .iter()
            .find(|(candidate, _)| candidate == name)
            .map_or("<unset>", |(_, value)| value.as_str())
    }

    /// The kitty-family variables that are set, sorted by name for a stable
    /// line. The families are an allowlist, so the whole environment is never
    /// dumped and unrelated secrets cannot reach a pasted report.
    fn family(&self) -> Vec<(&str, &str)> {
        let mut found: Vec<(&str, &str)> = self
            .0
            .iter()
            .filter(|(name, _)| FAMILIES.iter().any(|prefix| name.starts_with(prefix)))
            .map(|(name, value)| (name.as_str(), value.as_str()))
            .collect();
        found.sort_unstable();
        found
    }

    #[cfg(test)]
    fn of(pairs: &[(&str, &str)]) -> Self {
        Self(pairs.iter().map(|(name, value)| ((*name).to_string(), (*value).to_string())).collect())
    }
}

/// Reported in this order, `<unset>` when not set.
const ALWAYS_REPORTED: &[&str] = &[
    "TERM",
    "COLORTERM",
    "TERM_PROGRAM",
    "WT_SESSION",
    "TMUX",
    "VTE_VERSION",
    "KONSOLE_VERSION",
    "TERM_PROGRAM_VERSION",
];

/// Terminal-name families, reported only for variables that are set.
const FAMILIES: &[&str] = &["KITTY_", "WEZTERM_", "GHOSTTY_"];

/// Writes line 1 when enabled.
///
/// The line ends with CRLF: detection runs with the newline auto-return off
/// (raw mode on Unix, `DISABLE_NEWLINE_AUTO_RETURN` on Windows), so a bare LF
/// would leave the cursor at the end of the text.
pub fn log_detection(inputs: &DetectionInputs<'_>) {
    if enabled() {
        let line = detection_line(inputs, &Env::from_process(), &crate::version_string(), PLATFORM);
        eprint!("{line}\r\n");
    }
}

/// Writes line 2 when enabled.
pub fn log_image_decision(decision: ImageDecision) {
    if enabled() {
        eprintln!("{}", image_decision_line(decision));
    }
}

/// The one line-1 formatter both platforms use.
#[must_use]
fn detection_line(inputs: &DetectionInputs<'_>, env: &Env, version: &str, platform: &str) -> String {
    let mut line = format!("{PREFIX}wade={version} platform={platform} caps={:?}", inputs.capabilities);

    for name in ALWAYS_REPORTED {
        line.push_str(&format!(" {name}={}", env.value(name)));
    }
    for (name, value) in env.family() {
        line.push_str(&format!(" {name}={value}"));
    }
    if let Some(client) = inputs.tmux_client {
        line.push_str(&format!(
            " tmux_termtype={} tmux_features={} tmux_passthrough={}",
            client.termtype, client.features, client.passthrough
        ));
    }
    match &inputs.raw {
        RawReplies::Bytes(bytes) => line.push_str(&format!(" raw={}", escape_replies(bytes))),
        RawReplies::Absent(reason) => line.push_str(&format!(" raw=<none: {reason}>")),
    }
    line
}

/// The one line-2 formatter.
#[must_use]
fn image_decision_line(decision: ImageDecision) -> String {
    let protocol = decision.protocol.map_or("none", ImageProtocol::name);
    format!(
        "{PREFIX}setting={} protocol={protocol} effective={} image_previews_enabled={} protocol_resolved={}",
        decision.setting.name(),
        decision.effective,
        decision.previews_enabled,
        decision.protocol.is_some()
    )
}

/// The whole reply buffer, with control bytes visible and XTVERSION text
/// readable.
#[must_use]
fn escape_replies(bytes: &[u8]) -> String {
    String::from_utf8_lossy(bytes).escape_debug().to_string()
}

#[cfg(test)]
mod tests {
    use super::{
        DetectionInputs, Env, ImageDecision, PLATFORM, PREFIX, RawReplies, VARIABLE, detection_line, enabled_in,
        escape_replies, image_decision_line,
    };
    use crate::imaging::{ImageProtocol, ImageProtocolSetting};
    use crate::terminal_caps::{TerminalCapabilities, TmuxClient};

    #[test]
    fn only_one_true_or_yes_enable_the_dump() {
        assert_eq!(VARIABLE, "WADE_DEBUG_PROTOCOL");

        for value in [Some("1"), Some("true"), Some("TRUE"), Some("True"), Some("yes"), Some("YES")] {
            assert!(enabled_in(value), "{value:?}");
        }
        for value in
            [None, Some(""), Some("0"), Some("on"), Some("no"), Some("2"), Some("1 "), Some(" 1"), Some("true ")]
        {
            assert!(!enabled_in(value), "{value:?}");
        }
    }

    #[test]
    fn the_reported_platform_names_the_target() {
        #[cfg(windows)]
        assert_eq!(PLATFORM, "windows");
        #[cfg(not(windows))]
        assert_eq!(PLATFORM, "unix");
    }

    #[test]
    fn line_one_reports_caps_env_tmux_and_the_reply_bytes() {
        let caps = TerminalCapabilities {
            sixel_supported: true,
            cell_pixel_width: 10,
            cell_pixel_height: 20,
            kitty_graphics: false,
            iterm_images: false,
            tmux: None,
        };
        let tmux = TmuxClient {
            termtype: "kitty(0.39.1)".to_string(),
            features: "RGB,sixel".to_string(),
            passthrough: true,
            cell_size: Some((10, 20)),
            pane_top: 3,
            pane_left: 40,
        };
        let env = Env::of(&[
            ("TERM", "xterm-256color"),
            ("COLORTERM", "truecolor"),
            ("TERM_PROGRAM", "herdr"),
            ("WT_SESSION", ""),
            ("KITTY_WINDOW_ID", "1"),
            ("WEZTERM_PANE", "3"),
            ("GHOSTTY_RESOURCES_DIR", "/ghostty"),
        ]);
        let inputs = DetectionInputs {
            capabilities: caps,
            tmux_client: Some(&tmux),
            raw: RawReplies::Bytes(b"\x1b[?62;4c"),
        };

        assert_eq!(
            detection_line(&inputs, &env, "2.3.0+abc", "windows"),
            "wade-debug: wade=2.3.0+abc platform=windows caps=TerminalCapabilities { sixel_supported: true, \
             cell_pixel_width: 10, cell_pixel_height: 20, kitty_graphics: false, iterm_images: false, tmux: None } \
             TERM=xterm-256color COLORTERM=truecolor TERM_PROGRAM=herdr WT_SESSION= TMUX=<unset> VTE_VERSION=<unset> \
             KONSOLE_VERSION=<unset> TERM_PROGRAM_VERSION=<unset> GHOSTTY_RESOURCES_DIR=/ghostty KITTY_WINDOW_ID=1 \
             WEZTERM_PANE=3 tmux_termtype=kitty(0.39.1) tmux_features=RGB,sixel tmux_passthrough=true \
             raw=\\u{1b}[?62;4c"
        );
    }

    #[test]
    fn line_one_states_why_there_are_no_reply_bytes() {
        let inputs = DetectionInputs {
            capabilities: TerminalCapabilities::DEFAULT,
            tmux_client: None,
            raw: RawReplies::Absent("no queries on Windows"),
        };

        let line = detection_line(&inputs, &Env::of(&[]), "2.3.0", "windows");
        assert!(line.starts_with(PREFIX), "{line}");
        assert!(line.ends_with(" raw=<none: no queries on Windows>"), "{line}");
        assert!(line.contains(" caps=TerminalCapabilities { sixel_supported: false"), "{line}");
    }

    #[test]
    fn line_two_names_the_resolved_protocol_and_the_flag_it_comes_from() {
        let line = |setting, protocol, effective, previews_enabled| {
            image_decision_line(ImageDecision {
                setting,
                protocol,
                effective,
                previews_enabled,
            })
        };

        assert_eq!(
            line(ImageProtocolSetting::Auto, None, false, true),
            "wade-debug: setting=auto protocol=none effective=false image_previews_enabled=true protocol_resolved=false"
        );
        assert_eq!(
            line(ImageProtocolSetting::Auto, Some(ImageProtocol::Sixel), true, true),
            "wade-debug: setting=auto protocol=sixel effective=true image_previews_enabled=true protocol_resolved=true"
        );
        assert_eq!(
            line(ImageProtocolSetting::Kitty, None, false, false),
            "wade-debug: setting=kitty protocol=none effective=false image_previews_enabled=false protocol_resolved=false"
        );
        assert_eq!(
            line(ImageProtocolSetting::Iterm, Some(ImageProtocol::Iterm), true, true),
            "wade-debug: setting=iterm protocol=iterm effective=true image_previews_enabled=true protocol_resolved=true"
        );
    }

    #[test]
    fn reply_bytes_are_escaped_rather_than_written_raw() {
        assert_eq!(escape_replies(b"\x1b[?62;4c"), "\\u{1b}[?62;4c");
        assert_eq!(escape_replies(b"a\r\nb"), "a\\r\\nb");
        assert_eq!(escape_replies(b""), "");
        // Not valid UTF-8: the lossy replacement character, still one cell wide
        assert_eq!(escape_replies(b"\xff"), "\u{fffd}");
        // An XTVERSION reply stays readable; its terminating backslash is
        // escaped too
        assert_eq!(escape_replies(b"\x1bP>|kitty(0.39.1)\x1b\\"), r"\u{1b}P>|kitty(0.39.1)\u{1b}\\");
    }
}
