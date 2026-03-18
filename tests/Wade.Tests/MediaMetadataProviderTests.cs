using Wade.Preview;

namespace Wade.Tests;

public class MediaMetadataProviderTests
{
    private static PreviewContext MakeContext() =>
        new(
            PaneWidthCells: 60,
            PaneHeightCells: 30,
            CellPixelWidth: 8,
            CellPixelHeight: 16,
            IsCloudPlaceholder: false,
            IsBrokenSymlink: false,
            GitStatus: null,
            RepoRoot: null,
            DisabledTools: new HashSet<string>(),
            ZipPreviewEnabled: true,
            ImagePreviewsEnabled: true,
            ArchiveMetadataEnabled: true);

    // ── Extension matching ────────────────────────────────────────────────────

    [Theory]
    [InlineData("song.mp3")]
    [InlineData("song.MP3")]
    [InlineData("video.mp4")]
    [InlineData("video.mkv")]
    [InlineData("audio.flac")]
    [InlineData("audio.wav")]
    [InlineData("audio.ogg")]
    [InlineData("video.avi")]
    [InlineData("video.mov")]
    [InlineData("video.webm")]
    [InlineData("audio.m4a")]
    [InlineData("audio.opus")]
    public void CanProvideMetadata_MediaExtensions_ReturnsTrueWhenAvailable(string path)
    {
        if (!MediaMetadataProvider.IsAvailable)
        {
            return; // Skip if no CLI tool installed
        }

        var provider = new MediaMetadataProvider();
        Assert.True(provider.CanProvideMetadata(path, MakeContext()));
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("archive.zip")]
    [InlineData("app.exe")]
    [InlineData("image.png")]
    [InlineData("report.docx")]
    public void CanProvideMetadata_NonMediaExtensions_ReturnsFalse(string path)
    {
        var provider = new MediaMetadataProvider();
        Assert.False(provider.CanProvideMetadata(path, MakeContext()));
    }

    // ── ffprobe JSON parsing ──────────────────────────────────────────────────

    [Fact]
    public void ParseFfprobeJson_VideoAndAudio_ReturnsAllSections()
    {
        const string json = """
            {
              "streams": [
                {
                  "codec_type": "video",
                  "codec_long_name": "H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10",
                  "width": 1920,
                  "height": 1080,
                  "r_frame_rate": "30/1",
                  "bit_rate": "3200000"
                },
                {
                  "codec_type": "audio",
                  "codec_long_name": "AAC (Advanced Audio Coding)",
                  "channels": 2,
                  "channel_layout": "stereo",
                  "sample_rate": "48000",
                  "bit_rate": "128000"
                }
              ],
              "format": {
                "format_long_name": "QuickTime / MOV",
                "duration": "222.500000",
                "size": "8912345",
                "bit_rate": "3328000"
              }
            }
            """;

        var sections = MediaMetadataProvider.ParseFfprobeJson(json);

        Assert.NotNull(sections);
        string allText = FlattenSections(sections!);

        // General
        Assert.Contains("QuickTime / MOV", allText);
        Assert.Contains("3m 42s", allText);
        Assert.Contains("8.5 MB", allText);

        // Video
        Assert.Contains("H.264", allText);
        Assert.Contains("1920\u00d71080", allText);
        Assert.Contains("30.000 fps", allText);
        Assert.Contains("3,200 kbps", allText);

        // Audio
        Assert.Contains("AAC", allText);
        Assert.Contains("2 (stereo)", allText);
        Assert.Contains("48,000 Hz", allText);
        Assert.Contains("128 kbps", allText);
    }

    [Fact]
    public void ParseFfprobeJson_AudioOnly_ReturnsGeneralAndAudio()
    {
        const string json = """
            {
              "streams": [
                {
                  "codec_type": "audio",
                  "codec_long_name": "FLAC (Free Lossless Audio Codec)",
                  "channels": 2,
                  "channel_layout": "stereo",
                  "sample_rate": "44100",
                  "bit_rate": "880000"
                }
              ],
              "format": {
                "format_long_name": "raw FLAC",
                "duration": "195.200000",
                "size": "21504000",
                "bit_rate": "880000"
              }
            }
            """;

        var sections = MediaMetadataProvider.ParseFfprobeJson(json);

        Assert.NotNull(sections);
        string allText = FlattenSections(sections!);

        Assert.Contains("FLAC", allText);
        Assert.Contains("3m 15s", allText);
        Assert.Contains("44,100 Hz", allText);
        Assert.DoesNotContain("Video", allText);
    }

    [Fact]
    public void ParseFfprobeJson_EmptyStreams_ReturnsNull()
    {
        const string json = """
            {
              "streams": [],
              "format": {}
            }
            """;

        var sections = MediaMetadataProvider.ParseFfprobeJson(json);

        Assert.Null(sections);
    }

    // ── mediainfo JSON parsing ────────────────────────────────────────────────

    [Fact]
    public void ParseMediainfoJson_VideoAndAudio_ReturnsAllSections()
    {
        const string json = """
            {
              "media": {
                "track": [
                  {
                    "@type": "General",
                    "Format": "MPEG-4",
                    "Duration": "222.500",
                    "FileSize": "8912345",
                    "OverallBitRate": "3328000"
                  },
                  {
                    "@type": "Video",
                    "Format": "AVC",
                    "Width": "1920",
                    "Height": "1080",
                    "FrameRate": "30.000",
                    "BitRate": "3200000"
                  },
                  {
                    "@type": "Audio",
                    "Format": "AAC",
                    "Channels": "2",
                    "SamplingRate": "48000",
                    "BitRate": "128000"
                  }
                ]
              }
            }
            """;

        var sections = MediaMetadataProvider.ParseMediainfoJson(json);

        Assert.NotNull(sections);
        string allText = FlattenSections(sections!);

        Assert.Contains("MPEG-4", allText);
        Assert.Contains("3m 42s", allText);
        Assert.Contains("AVC", allText);
        Assert.Contains("1920\u00d71080", allText);
        Assert.Contains("AAC", allText);
        Assert.Contains("48,000 Hz", allText);
    }

    [Fact]
    public void ParseMediainfoJson_AudioOnly_ReturnsGeneralAndAudio()
    {
        const string json = """
            {
              "media": {
                "track": [
                  {
                    "@type": "General",
                    "Format": "FLAC",
                    "Duration": "195.200",
                    "FileSize": "21504000"
                  },
                  {
                    "@type": "Audio",
                    "Format": "FLAC",
                    "Channels": "2",
                    "SamplingRate": "44100",
                    "BitRate": "880000"
                  }
                ]
              }
            }
            """;

        var sections = MediaMetadataProvider.ParseMediainfoJson(json);

        Assert.NotNull(sections);
        string allText = FlattenSections(sections!);

        Assert.Contains("FLAC", allText);
        Assert.Contains("3m 15s", allText);
        Assert.DoesNotContain("Video", allText);
    }

    [Fact]
    public void ParseMediainfoJson_NoMedia_ReturnsNull()
    {
        const string json = """{ }""";

        var sections = MediaMetadataProvider.ParseMediainfoJson(json);

        Assert.Null(sections);
    }

    // ── Formatting helpers ────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(5, "5s")]
    [InlineData(65, "1m 05s")]
    [InlineData(222.5, "3m 42s")]
    [InlineData(3661, "1h 01m 01s")]
    public void FormatDuration_ReturnsExpected(double seconds, string expected)
    {
        Assert.Equal(expected, MediaMetadataProvider.FormatDuration(seconds));
    }

    [Theory]
    [InlineData(500, "500 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(8912345, "8.5 MB")]
    [InlineData(1073741824, "1.0 GB")]
    public void FormatFileSize_ReturnsExpected(long bytes, string expected)
    {
        Assert.Equal(expected, MediaMetadataProvider.FormatFileSize(bytes));
    }

    // ── Registry ──────────────────────────────────────────────────────────────

    [Fact]
    public void Registry_Mp4File_ReturnsMediaMetadataProviderWhenAvailable()
    {
        if (!MediaMetadataProvider.IsAvailable)
        {
            return; // Skip if no CLI tool installed
        }

        var providers = MetadataProviderRegistry.GetApplicableProviders("video.mp4", MakeContext());

        Assert.Contains(providers, p => p is MediaMetadataProvider);
    }

    private static string FlattenSections(MetadataSection[] sections)
    {
        var parts = new List<string>();
        foreach (MetadataSection s in sections)
        {
            if (s.Header is not null)
            {
                parts.Add(s.Header);
            }

            foreach (MetadataEntry e in s.Entries)
            {
                parts.Add($"{e.Label} {e.Value}");
            }
        }

        return string.Join('\n', parts);
    }
}
