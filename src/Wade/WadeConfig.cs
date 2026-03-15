using Wade.FileSystem;

namespace Wade;

internal sealed class WadeConfig
{
    public bool ShowIconsEnabled { get; set; } = true;
    public bool ImagePreviewsEnabled { get; set; } = true;
    public bool GlowMarkdownPreviewEnabled { get; set; } = false;
    public bool ShowHiddenFiles { get; set; } = false;
    public bool ShowSystemFiles { get; set; } = false;
    public SortMode SortMode { get; set; } = SortMode.Name;
    public bool SortAscending { get; set; } = true;
    public bool ConfirmDeleteEnabled { get; set; } = true;
    public bool PreviewPaneEnabled { get; set; } = true;
    public bool SizeColumnEnabled { get; set; } = true;
    public bool DateColumnEnabled { get; set; } = true;
    public bool CopySymlinksAsLinksEnabled { get; set; } = true;
    public bool ZipPreviewEnabled { get; set; } = true;
    public bool HexPreviewEnabled { get; set; } = true;
    public bool PdfPreviewEnabled { get; set; } = true;
    public bool TerminalTitleEnabled { get; set; } = true;
    public string StartPath { get; set; } = Directory.GetCurrentDirectory();
    public bool ShowConfig { get; set; } = false;
    public bool ShowHelp { get; set; } = false;
    public string ConfigFilePath { get; private set; } = "";
    public string? CwdFilePath { get; set; }

    public static WadeConfig Load(
        string[] args,
        string? configFilePath = null)
    {
        // Allow --config-file=<path> to override the config file location
        foreach (var arg in args)
        {
            if (arg.StartsWith("--config-file="))
            {
                configFilePath = arg["--config-file=".Length..];
                break;
            }
        }

        configFilePath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "wade", "config.toml");

        var config = new WadeConfig { ConfigFilePath = configFilePath };

        // Config file
        if (File.Exists(configFilePath))
        {
            foreach (var line in File.ReadAllLines(configFilePath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }

                int eq = trimmed.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }

                var key = trimmed[..eq].Trim();
                var value = trimmed[(eq + 1)..].Trim();

                // Strip inline comment
                int commentIdx = value.IndexOf('#');
                if (commentIdx >= 0)
                {
                    value = value[..commentIdx].Trim();
                }

                switch (key)
                {
                    case "show_icons_enabled":
                        config.ShowIconsEnabled = ParseBool(value, config.ShowIconsEnabled);
                        break;
                    case "image_previews_enabled":
                        config.ImagePreviewsEnabled = ParseBool(value, config.ImagePreviewsEnabled);
                        break;
                    case "glow_markdown_preview_enabled":
                        config.GlowMarkdownPreviewEnabled = ParseBool(value, config.GlowMarkdownPreviewEnabled);
                        break;
                    case "show_hidden_files":
                        config.ShowHiddenFiles = ParseBool(value, config.ShowHiddenFiles);
                        break;
                    case "show_system_files":
                        config.ShowSystemFiles = ParseBool(value, config.ShowSystemFiles);
                        break;
                    case "sort_mode":
                        if (Enum.TryParse<SortMode>(value, ignoreCase: true, out var sortMode))
                        {
                            config.SortMode = sortMode;
                        }

                        break;
                    case "sort_ascending":
                        config.SortAscending = ParseBool(value, config.SortAscending);
                        break;
                    case "confirm_delete_enabled":
                        config.ConfirmDeleteEnabled = ParseBool(value, config.ConfirmDeleteEnabled);
                        break;
                    case "preview_pane_enabled":
                        config.PreviewPaneEnabled = ParseBool(value, config.PreviewPaneEnabled);
                        break;
                    case "size_column_enabled":
                        config.SizeColumnEnabled = ParseBool(value, config.SizeColumnEnabled);
                        break;
                    case "date_column_enabled":
                        config.DateColumnEnabled = ParseBool(value, config.DateColumnEnabled);
                        break;
                    case "copy_symlinks_as_links_enabled":
                        config.CopySymlinksAsLinksEnabled = ParseBool(value, config.CopySymlinksAsLinksEnabled);
                        break;
                    case "zip_preview_enabled":
                        config.ZipPreviewEnabled = ParseBool(value, config.ZipPreviewEnabled);
                        break;
                    case "hex_preview_enabled":
                        config.HexPreviewEnabled = ParseBool(value, config.HexPreviewEnabled);
                        break;
                    case "pdf_preview_enabled":
                        config.PdfPreviewEnabled = ParseBool(value, config.PdfPreviewEnabled);
                        break;
                    case "terminal_title_enabled":
                        config.TerminalTitleEnabled = ParseBool(value, config.TerminalTitleEnabled);
                        break;
                    case "detail_columns_enabled":
                        // Backward compat: sets both columns
                        var detailBool = ParseBool(value, true);
                        config.SizeColumnEnabled = detailBool;
                        config.DateColumnEnabled = detailBool;
                        break;
                }
            }
        }

        // CLI flags
        foreach (var arg in args)
        {
            if (arg.StartsWith("--cwd-file="))
            {
                config.CwdFilePath = arg["--cwd-file=".Length..];
                continue;
            }

            switch (arg)
            {
                case "--show-config":
                    config.ShowConfig = true;
                    break;
                case "--help" or "-h":
                    config.ShowHelp = true;
                    break;
            }
        }

        // First non-flag arg is start path
        foreach (var arg in args)
        {
            if (!arg.StartsWith('-'))
            {
                config.StartPath = arg;
                break;
            }
        }

        // Expand ~ to home directory
        if (config.StartPath.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            config.StartPath = Path.Join(home, config.StartPath.AsSpan(1));
        }

        // Use explicit chars (not Path.DirectorySeparatorChar/AltDirectorySeparatorChar) so that
        // Windows-style backslashes are also stripped when running on Linux/macOS.
        var startPath = config.StartPath.TrimEnd('/', '\\');

        if (startPath.Length == 0)
        {
            startPath = config.StartPath[..1]; // "/" or "\" → preserve root
        }
        else if (startPath.Length == 2 && startPath[1] == ':' && startPath.Length < config.StartPath.Length)
        {
            startPath += '\\'; // "C:\" was trimmed to "C:" — restore the root separator
        }

        config.StartPath = startPath;

        return config;
    }

    internal void Save()
    {
        var dir = Path.GetDirectoryName(ConfigFilePath);
        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sortModeStr = SortMode.ToString().ToLowerInvariant();
        var content = $"""
            show_icons_enabled = {(ShowIconsEnabled ? "true" : "false")}
            image_previews_enabled = {(ImagePreviewsEnabled ? "true" : "false")}
            glow_markdown_preview_enabled = {(GlowMarkdownPreviewEnabled ? "true" : "false")}
            show_hidden_files = {(ShowHiddenFiles ? "true" : "false")}
            show_system_files = {(ShowSystemFiles ? "true" : "false")}
            sort_mode = {sortModeStr}
            sort_ascending = {(SortAscending ? "true" : "false")}
            confirm_delete_enabled = {(ConfirmDeleteEnabled ? "true" : "false")}
            preview_pane_enabled = {(PreviewPaneEnabled ? "true" : "false")}
            size_column_enabled = {(SizeColumnEnabled ? "true" : "false")}
            date_column_enabled = {(DateColumnEnabled ? "true" : "false")}
            copy_symlinks_as_links_enabled = {(CopySymlinksAsLinksEnabled ? "true" : "false")}
            zip_preview_enabled = {(ZipPreviewEnabled ? "true" : "false")}
            hex_preview_enabled = {(HexPreviewEnabled ? "true" : "false")}
            pdf_preview_enabled = {(PdfPreviewEnabled ? "true" : "false")}
            terminal_title_enabled = {(TerminalTitleEnabled ? "true" : "false")}
            """;

        File.WriteAllText(ConfigFilePath, content);
    }

    internal string ToJson()
    {
        var escapedPath = StartPath.Replace("\\", "\\\\");
        var sortModeStr = SortMode.ToString().ToLowerInvariant();
        return "{" +
            $"\"show_icons_enabled\":{(ShowIconsEnabled ? "true" : "false")}," +
            $"\"image_previews_enabled\":{(ImagePreviewsEnabled ? "true" : "false")}," +
            $"\"glow_markdown_preview_enabled\":{(GlowMarkdownPreviewEnabled ? "true" : "false")}," +
            $"\"show_hidden_files\":{(ShowHiddenFiles ? "true" : "false")}," +
            $"\"show_system_files\":{(ShowSystemFiles ? "true" : "false")}," +
            $"\"sort_mode\":\"{sortModeStr}\"," +
            $"\"sort_ascending\":{(SortAscending ? "true" : "false")}," +
            $"\"confirm_delete_enabled\":{(ConfirmDeleteEnabled ? "true" : "false")}," +
            $"\"preview_pane_enabled\":{(PreviewPaneEnabled ? "true" : "false")}," +
            $"\"size_column_enabled\":{(SizeColumnEnabled ? "true" : "false")}," +
            $"\"date_column_enabled\":{(DateColumnEnabled ? "true" : "false")}," +
            $"\"copy_symlinks_as_links_enabled\":{(CopySymlinksAsLinksEnabled ? "true" : "false")}," +
            $"\"zip_preview_enabled\":{(ZipPreviewEnabled ? "true" : "false")}," +
            $"\"hex_preview_enabled\":{(HexPreviewEnabled ? "true" : "false")}," +
            $"\"pdf_preview_enabled\":{(PdfPreviewEnabled ? "true" : "false")}," +
            $"\"terminal_title_enabled\":{(TerminalTitleEnabled ? "true" : "false")}," +
            $"\"start_path\":\"{escapedPath}\"" +
            "}";
    }

    internal static bool ParseBool(string value, bool fallback)
    {
        return value.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => true,
            "false" or "0" or "no" => false,
            _ => fallback,
        };
    }
}
