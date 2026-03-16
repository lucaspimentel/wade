using System.Diagnostics;
using System.Text.Json;
using Wade.Highlighting;

namespace Wade.Preview;

internal sealed class MediaPreviewProvider : IPreviewProvider
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

    public bool CanPreview(string path, PreviewContext context)
    {
        string ext = Path.GetExtension(path);
        return s_extensions.Contains(ext) && IsAvailable;
    }

    public PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct)
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

            List<StyledLine>? lines = s_ffprobeAvailable.Value
                ? ParseFfprobeJson(json)
                : ParseMediainfoJson(json);

            if (lines is null || lines.Count == 0)
            {
                return null;
            }

            return new PreviewResult
            {
                TextLines = lines.ToArray(),
                IsRendered = true,
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

    internal static List<StyledLine>? ParseFfprobeJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        var lines = new List<StyledLine>();

        // General info from "format"
        if (root.TryGetProperty("format", out JsonElement format))
        {
            lines.Add(new StyledLine("  Media File", null));
            lines.Add(new StyledLine("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", null));

            AddJsonField(lines, "Format", format, "format_long_name");
            AddDuration(lines, format, "duration");
            AddFileSize(lines, format, "size");
            AddBitRate(lines, format, "bit_rate");
        }

        // Streams
        if (root.TryGetProperty("streams", out JsonElement streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement stream in streams.EnumerateArray())
            {
                string? codecType = GetString(stream, "codec_type");

                if (codecType == "video")
                {
                    lines.Add(new StyledLine("", null));
                    lines.Add(new StyledLine("  Video", null));
                    lines.Add(new StyledLine("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", null));

                    AddJsonField(lines, "Codec", stream, "codec_long_name");
                    AddResolution(lines, stream, "width", "height");
                    AddFrameRate(lines, stream, "r_frame_rate");
                    AddBitRate(lines, stream, "bit_rate");
                }
                else if (codecType == "audio")
                {
                    lines.Add(new StyledLine("", null));
                    lines.Add(new StyledLine("  Audio", null));
                    lines.Add(new StyledLine("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", null));

                    AddJsonField(lines, "Codec", stream, "codec_long_name");
                    AddChannels(lines, stream, "channels", "channel_layout");
                    AddSampleRate(lines, stream, "sample_rate");
                    AddBitRate(lines, stream, "bit_rate");
                }
            }
        }

        return lines.Count > 2 ? lines : null;
    }

    internal static List<StyledLine>? ParseMediainfoJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("media", out JsonElement media)
            || !media.TryGetProperty("track", out JsonElement tracks)
            || tracks.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var lines = new List<StyledLine>();

        foreach (JsonElement track in tracks.EnumerateArray())
        {
            string? trackType = GetString(track, "@type");

            if (trackType == "General")
            {
                lines.Add(new StyledLine("  Media File", null));
                lines.Add(new StyledLine("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", null));

                AddJsonField(lines, "Format", track, "Format");
                AddDuration(lines, track, "Duration");
                AddFileSize(lines, track, "FileSize");
                AddBitRate(lines, track, "OverallBitRate");
            }
            else if (trackType == "Video")
            {
                lines.Add(new StyledLine("", null));
                lines.Add(new StyledLine("  Video", null));
                lines.Add(new StyledLine("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", null));

                AddJsonField(lines, "Codec", track, "Format");
                AddResolution(lines, track, "Width", "Height");
                AddJsonField(lines, "Frame rate", track, "FrameRate", " fps");
                AddBitRate(lines, track, "BitRate");
            }
            else if (trackType == "Audio")
            {
                lines.Add(new StyledLine("", null));
                lines.Add(new StyledLine("  Audio", null));
                lines.Add(new StyledLine("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", null));

                AddJsonField(lines, "Codec", track, "Format");
                AddChannels(lines, track, "Channels", null);
                AddSampleRate(lines, track, "SamplingRate");
                AddBitRate(lines, track, "BitRate");
            }
        }

        return lines.Count > 2 ? lines : null;
    }

    // ── Field helpers ─────────────────────────────────────────────────────────

    private static void AddJsonField(List<StyledLine> lines, string label, JsonElement element, string property, string suffix = "")
    {
        string? value = GetString(element, property);

        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(new StyledLine($"  {label,-14} {value}{suffix}", null));
        }
    }

    private static void AddDuration(List<StyledLine> lines, JsonElement element, string property)
    {
        string? value = GetString(element, property);

        if (value is not null && double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double seconds) && seconds > 0)
        {
            lines.Add(new StyledLine($"  {"Duration",-14} {FormatDuration(seconds)}", null));
        }
    }

    private static void AddFileSize(List<StyledLine> lines, JsonElement element, string property)
    {
        string? value = GetString(element, property);

        if (value is not null && long.TryParse(value, out long bytes) && bytes > 0)
        {
            lines.Add(new StyledLine($"  {"File size",-14} {FormatFileSize(bytes)}", null));
        }
    }

    private static void AddBitRate(List<StyledLine> lines, JsonElement element, string property)
    {
        string? value = GetString(element, property);

        if (value is not null && long.TryParse(value, out long bps) && bps > 0)
        {
            lines.Add(new StyledLine($"  {"Bit rate",-14} {bps / 1000:N0} kbps", null));
        }
    }

    private static void AddResolution(List<StyledLine> lines, JsonElement element, string widthProp, string heightProp)
    {
        string? w = GetString(element, widthProp);
        string? h = GetString(element, heightProp);

        if (w is not null && h is not null)
        {
            lines.Add(new StyledLine($"  {"Resolution",-14} {w}\u00d7{h}", null));
        }
    }

    private static void AddFrameRate(List<StyledLine> lines, JsonElement element, string property)
    {
        string? value = GetString(element, property);

        if (value is null)
        {
            return;
        }

        // ffprobe uses fraction format like "30/1" or "30000/1001"
        string[] parts = value.Split('/');

        if (parts.Length == 2
            && double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out double num)
            && double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out double den)
            && den > 0)
        {
            double fps = num / den;
            lines.Add(new StyledLine($"  {"Frame rate",-14} {fps:F3} fps", null));
        }
        else if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double directFps))
        {
            lines.Add(new StyledLine($"  {"Frame rate",-14} {directFps:F3} fps", null));
        }
    }

    private static void AddChannels(List<StyledLine> lines, JsonElement element, string channelsProp, string? layoutProp)
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
        lines.Add(new StyledLine($"  {"Channels",-14} {display}", null));
    }

    private static void AddSampleRate(List<StyledLine> lines, JsonElement element, string property)
    {
        string? value = GetString(element, property);

        if (value is not null && int.TryParse(value, out int hz) && hz > 0)
        {
            lines.Add(new StyledLine($"  {"Sample rate",-14} {hz:N0} Hz", null));
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
