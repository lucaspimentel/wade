using System.Reflection;
using Wade;

var config = WadeConfig.Load(args);

if (config.ShowVersion)
{
    PrintVersion();
    return 0;
}

if (config.ShowHelp)
{
    PrintHelp();
    return 0;
}

if (config.ShowConfig)
{
    Console.WriteLine(config.ToJson());
    return 0;
}

// Validate the start path before entering the TUI
string fullStartPath = Path.GetFullPath(config.StartPath);

if (File.Exists(fullStartPath))
{
    // Path points to a file — open its parent directory and select the file
    string? parent = Path.GetDirectoryName(fullStartPath);

    if (parent is not null)
    {
        config.StartPath = parent;
        config.StartFileName = Path.GetFileName(fullStartPath);
    }
}
else if (!Directory.Exists(fullStartPath))
{
    Console.Error.WriteLine($"wade: path does not exist: {config.StartPath}");
    return 1;
}

string? finalPath = new App(config).Run();

if (config.CwdFilePath is not null && finalPath is not null)
{
    try
    {
        File.WriteAllText(config.CwdFilePath, finalPath);
    }
    catch
    {
        /* silently ignore */
    }
}

return 0;

static void PrintVersion()
{
    string version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                     ?? typeof(App).Assembly.GetName().Version?.ToString()
                     ?? "unknown";

    Console.WriteLine($"wade {version}");
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        wade — TUI file browser

        Usage: wade [options] [path]

        Options:
          -h, --help                      Show this help and exit
          --version                       Show version and exit
          --show-config                   Print resolved config as JSON and exit
          --config-file=<path>            Use a custom config file

        Keybindings:
          ?                               Show keybindings in-app

        Config file: ~/.config/wade/config.toml

          show_icons_enabled = true
          image_previews_enabled = true
          show_hidden_files = false
          sort_mode = name                # name, modified, size, extension
          sort_ascending = true
          confirm_delete_enabled = true
          preview_pane_enabled = true
          detail_columns_enabled = true
        """);
}
