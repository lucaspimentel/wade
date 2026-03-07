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

new App(config).Run();

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
          Up / k                          Move selection up
          Down / j                        Move selection down
          Right / l / Enter               Open directory
          Left / h / Backspace            Go to parent directory
          Page Up / Page Down             Scroll by page
          Home / End                      Jump to first / last item
          Left Click                      Select / Open
          Scroll                          Navigate up/down
          Ctrl+R                          Refresh
          ?                               Show help overlay
          q / Escape                      Quit

        Config file: ~/.config/wade/config.toml

          show_icons_enabled = true
          image_previews_enabled = true
        """);
}
