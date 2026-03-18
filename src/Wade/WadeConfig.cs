using Wade.FileSystem;

namespace Wade;

internal sealed class WadeConfig
{
    public bool ShowIconsEnabled { get; set; } = true;
    public bool ImagePreviewsEnabled { get; set; } = true;
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
    public bool TerminalTitleEnabled { get; set; } = true;
    public bool GitStatusEnabled { get; set; } = true;
    public bool FileMetadataEnabled { get; set; } = true;
    public bool FilePreviewsEnabled { get; set; } = true;
    public bool ArchiveMetadataEnabled { get; set; } = true;
    public HashSet<string> DisabledTools { get; set; } = [];
    public string StartPath { get; set; } = Directory.GetCurrentDirectory();
    public bool ShowConfig { get; set; } = false;
    public bool ShowHelp { get; set; } = false;
    public bool ShowVersion { get; set; } = false;
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
                    case "terminal_title_enabled":
                        config.TerminalTitleEnabled = ParseBool(value, config.TerminalTitleEnabled);
                        break;
                    case "git_status_enabled":
                        config.GitStatusEnabled = ParseBool(value, config.GitStatusEnabled);
                        break;
                    case "file_metadata_enabled":
                        config.FileMetadataEnabled = ParseBool(value, config.FileMetadataEnabled);
                        break;
                    case "file_previews_enabled":
                        config.FilePreviewsEnabled = ParseBool(value, config.FilePreviewsEnabled);
                        break;
                    case "archive_metadata_enabled":
                        config.ArchiveMetadataEnabled = ParseBool(value, config.ArchiveMetadataEnabled);
                        break;
                    case "disabled_tools":
                        foreach (string tool in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            config.DisabledTools.Add(tool);
                        }

                        break;
                    case "detail_columns_enabled":
                        // Backward compat: sets both columns
                        var detailBool = ParseBool(value, true);
                        config.SizeColumnEnabled = detailBool;
                        config.DateColumnEnabled = detailBool;
                        break;
                    // Backward compat: map old per-tool booleans to disabled_tools
                    case "glow_markdown_preview_enabled":
                        if (!ParseBool(value, true))
                        {
                            config.DisabledTools.Add("glow");
                        }

                        break;
                    case "pdf_preview_enabled":
                        if (!ParseBool(value, true))
                        {
                            config.DisabledTools.Add("pdftopng");
                            config.DisabledTools.Add("pdfinfo");
                        }

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
                case "--version":
                    config.ShowVersion = true;
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
            terminal_title_enabled = {(TerminalTitleEnabled ? "true" : "false")}
            git_status_enabled = {(GitStatusEnabled ? "true" : "false")}
            file_metadata_enabled = {(FileMetadataEnabled ? "true" : "false")}
            file_previews_enabled = {(FilePreviewsEnabled ? "true" : "false")}
            archive_metadata_enabled = {(ArchiveMetadataEnabled ? "true" : "false")}
            """;

        if (DisabledTools.Count > 0)
        {
            content += $"\ndisabled_tools = {string.Join(",", DisabledTools.Order())}";
        }

        File.WriteAllText(ConfigFilePath, content);
    }

    internal string ToJson()
    {
        var escapedPath = StartPath.Replace("\\", "\\\\");
        var sortModeStr = SortMode.ToString().ToLowerInvariant();
        string disabledToolsJson = DisabledTools.Count > 0
            ? "[" + string.Join(",", DisabledTools.Order().Select(t => $"\"{t}\"")) + "]"
            : "[]";

        return "{" +
            $"\"show_icons_enabled\":{(ShowIconsEnabled ? "true" : "false")}," +
            $"\"image_previews_enabled\":{(ImagePreviewsEnabled ? "true" : "false")}," +
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
            $"\"disabled_tools\":{disabledToolsJson}," +
            $"\"terminal_title_enabled\":{(TerminalTitleEnabled ? "true" : "false")}," +
            $"\"git_status_enabled\":{(GitStatusEnabled ? "true" : "false")}," +
            $"\"file_metadata_enabled\":{(FileMetadataEnabled ? "true" : "false")}," +
            $"\"file_previews_enabled\":{(FilePreviewsEnabled ? "true" : "false")}," +
            $"\"archive_metadata_enabled\":{(ArchiveMetadataEnabled ? "true" : "false")}," +
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
