using Wade.FileSystem;

namespace Wade;

internal sealed class WadeConfig
{
    public bool ShowIconsEnabled { get; set; } = true;

    public bool ImagePreviewsEnabled { get; set; } = true;

    public bool ShowHiddenFiles { get; set; }

    public bool ShowSystemFiles { get; set; }

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

    public bool DirSizeSsdEnabled { get; set; } = true;

    public bool DirSizeHddEnabled { get; set; }

    public bool DirSizeNetworkEnabled { get; set; }

    public bool PdfPreviewEnabled { get; set; } = true;

    public bool PdfMetadataEnabled { get; set; } = true;

    public bool MarkdownPreviewEnabled { get; set; } = true;

    public bool FfprobeEnabled { get; set; } = true;

    public bool MediainfoEnabled { get; set; } = true;

    public string StartPath { get; set; } = Directory.GetCurrentDirectory();

    public bool ShowConfig { get; set; }

    public bool ShowHelp { get; set; }

    public bool ShowVersion { get; set; }

    public string ConfigFilePath { get; private set; } = "";

    public string? CwdFilePath { get; set; }

    public static WadeConfig Load(
        string[] args,
        string? configFilePath = null)
    {
        // Allow --config-file=<path> to override the config file location
        foreach (string arg in args)
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
            foreach (string line in File.ReadAllLines(configFilePath))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }

                int eq = trimmed.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }

                string key = trimmed[..eq].Trim();
                string value = trimmed[(eq + 1)..].Trim();

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
                        if (Enum.TryParse(value, ignoreCase: true, out SortMode sortMode))
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
                    case "dir_size_ssd_enabled":
                        config.DirSizeSsdEnabled = ParseBool(value, config.DirSizeSsdEnabled);
                        break;
                    case "dir_size_hdd_enabled":
                        config.DirSizeHddEnabled = ParseBool(value, config.DirSizeHddEnabled);
                        break;
                    case "dir_size_network_enabled":
                        config.DirSizeNetworkEnabled = ParseBool(value, config.DirSizeNetworkEnabled);
                        break;
                    case "pdf_preview_enabled":
                        config.PdfPreviewEnabled = ParseBool(value, config.PdfPreviewEnabled);
                        break;
                    case "pdf_metadata_enabled":
                        config.PdfMetadataEnabled = ParseBool(value, config.PdfMetadataEnabled);
                        break;
                    case "markdown_preview_enabled":
                        config.MarkdownPreviewEnabled = ParseBool(value, config.MarkdownPreviewEnabled);
                        break;
                    case "ffprobe_enabled":
                        config.FfprobeEnabled = ParseBool(value, config.FfprobeEnabled);
                        break;
                    case "mediainfo_enabled":
                        config.MediainfoEnabled = ParseBool(value, config.MediainfoEnabled);
                        break;
                    case "detail_columns_enabled":
                        // Backward compat: sets both columns
                        bool detailBool = ParseBool(value, true);
                        config.SizeColumnEnabled = detailBool;
                        config.DateColumnEnabled = detailBool;
                        break;
                    case "disabled_tools":
                        foreach (string tool in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            switch (tool)
                            {
                                case "pdftopng":
                                    config.PdfPreviewEnabled = false;
                                    break;
                                case "pdfinfo":
                                    config.PdfMetadataEnabled = false;
                                    break;
                                case "markdown_preview":
                                    config.MarkdownPreviewEnabled = false;
                                    break;
                                case "ffprobe":
                                    config.FfprobeEnabled = false;
                                    break;
                                case "mediainfo":
                                    config.MediainfoEnabled = false;
                                    break;
                            }
                        }

                        break;
                }
            }
        }

        // CLI flags
        foreach (string arg in args)
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
        foreach (string arg in args)
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
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            config.StartPath = Path.Join(home, config.StartPath.AsSpan(1));
        }

        // Use explicit chars (not Path.DirectorySeparatorChar/AltDirectorySeparatorChar) so that
        // Windows-style backslashes are also stripped when running on Linux/macOS.
        string startPath = config.StartPath.TrimEnd('/', '\\');

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
        string? dir = Path.GetDirectoryName(ConfigFilePath);
        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string sortModeStr = SortMode.ToString().ToLowerInvariant();
        string content = $"""
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
                          dir_size_ssd_enabled = {(DirSizeSsdEnabled ? "true" : "false")}
                          dir_size_hdd_enabled = {(DirSizeHddEnabled ? "true" : "false")}
                          dir_size_network_enabled = {(DirSizeNetworkEnabled ? "true" : "false")}
                          pdf_preview_enabled = {(PdfPreviewEnabled ? "true" : "false")}
                          pdf_metadata_enabled = {(PdfMetadataEnabled ? "true" : "false")}
                          markdown_preview_enabled = {(MarkdownPreviewEnabled ? "true" : "false")}
                          ffprobe_enabled = {(FfprobeEnabled ? "true" : "false")}
                          mediainfo_enabled = {(MediainfoEnabled ? "true" : "false")}
                          """;

        File.WriteAllText(ConfigFilePath, content);
    }

    internal string ToJson()
    {
        string escapedPath = StartPath.Replace("\\", "\\\\");
        string sortModeStr = SortMode.ToString().ToLowerInvariant();

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
               $"\"terminal_title_enabled\":{(TerminalTitleEnabled ? "true" : "false")}," +
               $"\"git_status_enabled\":{(GitStatusEnabled ? "true" : "false")}," +
               $"\"file_metadata_enabled\":{(FileMetadataEnabled ? "true" : "false")}," +
               $"\"file_previews_enabled\":{(FilePreviewsEnabled ? "true" : "false")}," +
               $"\"archive_metadata_enabled\":{(ArchiveMetadataEnabled ? "true" : "false")}," +
               $"\"dir_size_ssd_enabled\":{(DirSizeSsdEnabled ? "true" : "false")}," +
               $"\"dir_size_hdd_enabled\":{(DirSizeHddEnabled ? "true" : "false")}," +
               $"\"dir_size_network_enabled\":{(DirSizeNetworkEnabled ? "true" : "false")}," +
               $"\"pdf_preview_enabled\":{(PdfPreviewEnabled ? "true" : "false")}," +
               $"\"pdf_metadata_enabled\":{(PdfMetadataEnabled ? "true" : "false")}," +
               $"\"markdown_preview_enabled\":{(MarkdownPreviewEnabled ? "true" : "false")}," +
               $"\"ffprobe_enabled\":{(FfprobeEnabled ? "true" : "false")}," +
               $"\"mediainfo_enabled\":{(MediainfoEnabled ? "true" : "false")}," +
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
