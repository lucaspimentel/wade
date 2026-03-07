namespace Wade;

internal sealed class WadeConfig
{
    public bool ShowIconsEnabled { get; set; } = true;
    public bool ImagePreviewsEnabled { get; set; } = true;
    public bool ShowHiddenFiles { get; set; } = false;
    public string SortMode { get; set; } = "name";
    public bool SortAscending { get; set; } = true;
    public string StartPath { get; set; } = Directory.GetCurrentDirectory();
    public bool ShowConfig { get; set; } = false;
    public bool ShowHelp { get; set; } = false;

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

        var config = new WadeConfig();

        // Config file
        if (File.Exists(configFilePath))
        {
            foreach (var line in File.ReadAllLines(configFilePath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                    continue;

                int eq = trimmed.IndexOf('=');
                if (eq < 0)
                    continue;

                var key = trimmed[..eq].Trim();
                var value = trimmed[(eq + 1)..].Trim();

                // Strip inline comment
                int commentIdx = value.IndexOf('#');
                if (commentIdx >= 0)
                    value = value[..commentIdx].Trim();

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
                    case "sort_mode":
                        config.SortMode = value.ToLowerInvariant();
                        break;
                    case "sort_ascending":
                        config.SortAscending = ParseBool(value, config.SortAscending);
                        break;
                }
            }
        }

        // CLI flags
        foreach (var arg in args)
        {
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
            if (!arg.StartsWith("--"))
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
        config.StartPath = config.StartPath.TrimEnd('/', '\\');

        return config;
    }

    internal string ToJson()
    {
        var escapedPath = StartPath.Replace("\\", "\\\\");
        return $"{{\"show_icons_enabled\":{(ShowIconsEnabled ? "true" : "false")},\"image_previews_enabled\":{(ImagePreviewsEnabled ? "true" : "false")},\"show_hidden_files\":{(ShowHiddenFiles ? "true" : "false")},\"sort_mode\":\"{SortMode}\",\"sort_ascending\":{(SortAscending ? "true" : "false")},\"start_path\":\"{escapedPath}\"}}";
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
