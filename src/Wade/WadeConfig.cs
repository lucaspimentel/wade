namespace Wade;

internal sealed class WadeConfig
{
    public bool ShowIconsEnabled { get; set; } = true;
    public bool ImagePreviewsEnabled { get; set; } = false;
    public string StartPath { get; set; } = Directory.GetCurrentDirectory();
    public bool ShowConfig { get; set; } = false;

    public static WadeConfig Load(
        string[] args,
        string? configFilePath = null,
        IEnvironmentVariablesProvider? env = null)
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
        env ??= new SystemEnvironmentVariablesProvider();

        var config = new WadeConfig();

        // Tier 1: config file
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
                }
            }
        }

        // Tier 2: environment variables
        var showIcons = env.GetEnvironmentVariable("WADE_SHOW_ICONS_ENABLED");
        if (showIcons is not null)
            config.ShowIconsEnabled = ParseBool(showIcons, config.ShowIconsEnabled);

        var imagePreviews = env.GetEnvironmentVariable("WADE_IMAGE_PREVIEWS_ENABLED");
        if (imagePreviews is not null)
            config.ImagePreviewsEnabled = ParseBool(imagePreviews, config.ImagePreviewsEnabled);

        // Tier 3: CLI flags
        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--show-icons-enabled":
                    config.ShowIconsEnabled = true;
                    break;
                case "--no-show-icons-enabled":
                    config.ShowIconsEnabled = false;
                    break;
                case "--image-previews-enabled":
                    config.ImagePreviewsEnabled = true;
                    break;
                case "--no-image-previews-enabled":
                    config.ImagePreviewsEnabled = false;
                    break;
                case "--show-config":
                    config.ShowConfig = true;
                    break;
                default:
                    if (arg.StartsWith("--show-icons-enabled="))
                        config.ShowIconsEnabled = ParseBool(arg["--show-icons-enabled=".Length..], config.ShowIconsEnabled);
                    else if (arg.StartsWith("--image-previews-enabled="))
                        config.ImagePreviewsEnabled = ParseBool(arg["--image-previews-enabled=".Length..], config.ImagePreviewsEnabled);
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

        config.StartPath = config.StartPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return config;
    }

    internal string ToJson()
    {
        var escapedPath = StartPath.Replace("\\", "\\\\");
        return $"{{\"show_icons_enabled\":{(ShowIconsEnabled ? "true" : "false")},\"image_previews_enabled\":{(ImagePreviewsEnabled ? "true" : "false")},\"start_path\":\"{escapedPath}\"}}";
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
