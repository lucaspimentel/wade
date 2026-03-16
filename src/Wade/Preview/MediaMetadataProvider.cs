using System.Diagnostics;
using System.Text.Json;

namespace Wade.Preview;

internal sealed class MediaMetadataProvider : IMetadataProvider
{
    private static readonly Lazy<bool> s_ffprobeAvailable = new(CheckFfprobe);
    private static readonly Lazy<bool> s_mediainfoAvailable = new(CheckMediainfo);

    private static readonly HashSet<string> s_extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Audio
        ".mp3", ".flac", ".wav", ".ogg", ".aac", ".wma", ".m4a", ".opus", ".aiff",
        // Video
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v", ".ts", ".mpg", ".mpeg",
    };

    public string Label => "Media info";

    internal static bool IsAvailable => s_ffprobeAvailable.Value || s_mediainfoAvailable.Value;

    public bool CanProvideMetadata(string path, PreviewContext context)
    {
        string ext = Path.GetExtension(path);
        return s_extensions.Contains(ext) && IsAvailable;
    }

    public MetadataResult? GetMetadata(string path, PreviewContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            string? json = s_ffprobeAvailable.Value
                ? RunFfprobe(path)
                : s_mediainfoAvailable.Value
                    ? RunMediainfo(path)
                    : null;

            if (json is null)
            {
                return null;
            }

            ct.ThrowIfCancellationRequested();

            MetadataSection[]? sections = s_ffprobeAvailable.Value
                ? ParseFfprobeJson(json)
                : ParseMediainfoJson(json);

            if (sections is null || sections.Length == 0)
            {
                return null;
            }

            return new MetadataResult
            {
                Sections = sections,
                FileTypeLabel = GetFileTypeLabel(path),
            };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // ── Process execution ─────────────────────────────────────────────────────

    private static string? RunFfprobe(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            ArgumentList = { "-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", path },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        return RunProcess(psi);
    }

    private static string? RunMediainfo(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "mediainfo",
            ArgumentList = { "--Output=JSON", path },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        return RunProcess(psi);
    }

    private static string? RunProcess(ProcessStartInfo psi)
    {
        using var process = Process.Start(psi);

        if (process is null)
        {
            return null;
        }

        string output = process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(5000))
        {
            try { process.Kill(); }
            catch { /* best effort */ }

            return null;
        }

        return process.ExitCode == 0 ? output : null;
    }

    // ── JSON parsing ──────────────────────────────────────────────────────────

    internal static MetadataSection[]? ParseFfprobeJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        var sections = new List<MetadataSection>();

        // General info from "format"
        if (root.TryGetProperty("format", out JsonElement format))
        {
            var entries = new List<MetadataEntry>();

            AddJsonEntry(entries, "Format", format, "format_long_name");
            AddDurationEntry(entries, format, "duration");
            AddFileSizeEntry(entries, format, "size");
            AddBitRateEntry(entries, format, "bit_rate");

            if (entries.Count > 0)
            {
                sections.Add(new MetadataSection("Media File", entries.ToArray()));
            }
        }

        // Streams
        if (root.TryGetProperty("streams", out JsonElement streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement stream in streams.EnumerateArray())
            {
                string? codecType = GetString(stream, "codec_type");

                if (codecType == "video")
                {
                    var entries = new List<MetadataEntry>();
                    AddJsonEntry(entries, "Codec", stream, "codec_long_name");
                    AddResolutionEntry(entries, stream, "width", "height");
                    AddFrameRateEntry(entries, stream, "r_frame_rate");
                    AddBitRateEntry(entries, stream, "bit_rate");

                    if (entries.Count > 0)
                    {
                        sections.Add(new MetadataSection("Video", entries.ToArray()));
                    }
                }
                else if (codecType == "audio")
                {
                    var entries = new List<MetadataEntry>();
                    AddJsonEntry(entries, "Codec", stream, "codec_long_name");
                    AddChannelsEntry(entries, stream, "channels", "channel_layout");
                    AddSampleRateEntry(entries, stream, "sample_rate");
                    AddBitRateEntry(entries, stream, "bit_rate");

                    if (entries.Count > 0)
                    {
                        sections.Add(new MetadataSection("Audio", entries.ToArray()));
                    }
                }
            }
        }

        return sections.Count > 0 ? sections.ToArray() : null;
    }

    internal static MetadataSection[]? ParseMediainfoJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("media", out JsonElement media)
            || !media.TryGetProperty("track", out JsonElement tracks)
            || tracks.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var sections = new List<MetadataSection>();

        foreach (JsonElement track in tracks.EnumerateArray())
        {
            string? trackType = GetString(track, "@type");

            if (trackType == "General")
            {
                var entries = new List<MetadataEntry>();
                AddJsonEntry(entries, "Format", track, "Format");
                AddDurationEntry(entries, track, "Duration");
                AddFileSizeEntry(entries, track, "FileSize");
                AddBitRateEntry(entries, track, "OverallBitRate");

                if (entries.Count > 0)
                {
                    sections.Add(new MetadataSection("Media File", entries.ToArray()));
                }
            }
            else if (trackType == "Video")
            {
                var entries = new List<MetadataEntry>();
                AddJsonEntry(entries, "Codec", track, "Format");
                AddResolutionEntry(entries, track, "Width", "Height");
                AddJsonEntry(entries, "Frame rate", track, "FrameRate", " fps");
                AddBitRateEntry(entries, track, "BitRate");

                if (entries.Count > 0)
                {
                    sections.Add(new MetadataSection("Video", entries.ToArray()));
                }
            }
            else if (trackType == "Audio")
            {
                var entries = new List<MetadataEntry>();
                AddJsonEntry(entries, "Codec", track, "Format");
                AddChannelsEntry(entries, track, "Channels", null);
                AddSampleRateEntry(entries, track, "SamplingRate");
                AddBitRateEntry(entries, track, "BitRate");

                if (entries.Count > 0)
                {
                    sections.Add(new MetadataSection("Audio", entries.ToArray()));
                }
            }
        }

        return sections.Count > 0 ? sections.ToArray() : null;
    }

    // ── Entry helpers ─────────────────────────────────────────────────────────

    private static void AddJsonEntry(List<MetadataEntry> entries, string label, JsonElement element, string property, string suffix = "")
    {
        string? value = GetString(element, property);

        if (!string.IsNullOrWhiteSpace(value))
        {
            entries.Add(new MetadataEntry(label, value + suffix));
        }
    }

    private static void AddDurationEntry(List<MetadataEntry> entries, JsonElement element, string property)
    {
        string? value = GetString(element, property);

        if (value is not null && double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double seconds) && seconds > 0)
        {
            entries.Add(new MetadataEntry("Duration", FormatDuration(seconds)));
        }
    }

    private static void AddFileSizeEntry(List<MetadataEntry> entries, JsonElement element, string property)
    {
        string? value = GetString(element, property);

        if (value is not null && long.TryParse(value, out long bytes) && bytes > 0)
        {
            entries.Add(new MetadataEntry("File size", FormatFileSize(bytes)));
        }
    }

    private static void AddBitRateEntry(List<MetadataEntry> entries, JsonElement element, string property)
    {
        string? value = GetString(element, property);

        if (value is not null && long.TryParse(value, out long bps) && bps > 0)
        {
            entries.Add(new MetadataEntry("Bit rate", $"{bps / 1000:N0} kbps"));
        }
    }

    private static void AddResolutionEntry(List<MetadataEntry> entries, JsonElement element, string widthProp, string heightProp)
    {
        string? w = GetString(element, widthProp);
        string? h = GetString(element, heightProp);

        if (w is not null && h is not null)
        {
            entries.Add(new MetadataEntry("Resolution", $"{w}\u00d7{h}"));
        }
    }

    private static void AddFrameRateEntry(List<MetadataEntry> entries, JsonElement element, string property)
    {
        string? value = GetString(element, property);

        if (value is null)
        {
            return;
        }

        string[] parts = value.Split('/');

        if (parts.Length == 2
            && double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out double num)
            && double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out double den)
            && den > 0)
        {
            double fps = num / den;
            entries.Add(new MetadataEntry("Frame rate", $"{fps:F3} fps"));
        }
        else if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double directFps))
        {
            entries.Add(new MetadataEntry("Frame rate", $"{directFps:F3} fps"));
        }
    }

    private static void AddChannelsEntry(List<MetadataEntry> entries, JsonElement element, string channelsProp, string? layoutProp)
    {
        string? channels = GetString(element, channelsProp);

        if (channels is null)
        {
            return;
        }

        string? layout = layoutProp is not null ? GetString(element, layoutProp) : null;

        if (string.IsNullOrWhiteSpace(layout) && int.TryParse(channels, out int ch))
        {
            layout = ch switch
            {
                1 => "mono",
                2 => "stereo",
                6 => "5.1",
                8 => "7.1",
                _ => null,
            };
        }

        string display = layout is not null ? $"{channels} ({layout})" : channels;
        entries.Add(new MetadataEntry("Channels", display));
    }

    private static void AddSampleRateEntry(List<MetadataEntry> entries, JsonElement element, string property)
    {
        string? value = GetString(element, property);

        if (value is not null && int.TryParse(value, out int hz) && hz > 0)
        {
            entries.Add(new MetadataEntry("Sample rate", $"{hz:N0} Hz"));
        }
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    internal static string FormatDuration(double totalSeconds)
    {
        int hours = (int)(totalSeconds / 3600);
        int minutes = (int)(totalSeconds % 3600 / 60);
        int seconds = (int)(totalSeconds % 60);

        if (hours > 0)
        {
            return $"{hours}h {minutes:D2}m {seconds:D2}s";
        }

        return minutes > 0 ? $"{minutes}m {seconds:D2}s" : $"{seconds}s";
    }

    internal static string FormatFileSize(long bytes)
    {
        const double KB = 1024;
        const double MB = KB * 1024;
        const double GB = MB * 1024;

        return bytes switch
        {
            >= (long)GB => $"{bytes / GB:F1} GB",
            >= (long)MB => $"{bytes / MB:F1} MB",
            >= (long)KB => $"{bytes / KB:F1} KB",
            _ => $"{bytes} B",
        };
    }

    private static string GetFileTypeLabel(string path)
    {
        string ext = Path.GetExtension(path).ToUpperInvariant();

        return ext switch
        {
            ".MP3" => "MP3",
            ".FLAC" => "FLAC",
            ".WAV" => "WAV",
            ".OGG" => "OGG",
            ".AAC" => "AAC",
            ".WMA" => "WMA",
            ".M4A" => "M4A",
            ".OPUS" => "Opus",
            ".AIFF" => "AIFF",
            ".MP4" => "MP4",
            ".MKV" => "MKV",
            ".AVI" => "AVI",
            ".MOV" => "MOV",
            ".WMV" => "WMV",
            ".WEBM" => "WebM",
            ".FLV" => "FLV",
            ".M4V" => "M4V",
            ".TS" => "MPEG-TS",
            ".MPG" or ".MPEG" => "MPEG",
            _ => ext.TrimStart('.'),
        };
    }

    // ── Availability checks ───────────────────────────────────────────────────

    private static bool CheckFfprobe()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);

            if (process is null)
            {
                return false;
            }

            process.StandardOutput.ReadToEnd();
            return process.WaitForExit(3000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckMediainfo()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "mediainfo",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);

            if (process is null)
            {
                return false;
            }

            process.StandardOutput.ReadToEnd();
            return process.WaitForExit(3000);
        }
        catch
        {
            return false;
        }
    }
}
