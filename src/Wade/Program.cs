using Wade;

var config = WadeConfig.Load(args);

if (config.ShowHelp)
{
    PrintHelp();
    return;
}

if (config.ShowConfig)
{
    Console.WriteLine(config.ToJson());
    return;
}

var finalPath = new App(config).Run();

if (config.CwdFilePath is not null && finalPath is not null)
{
    try { File.WriteAllText(config.CwdFilePath, finalPath); }
    catch { /* silently ignore */ }
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        wade — TUI file browser

        Usage: wade [options] [path]

        Options:
          -h, --help                      Show this help and exit
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
