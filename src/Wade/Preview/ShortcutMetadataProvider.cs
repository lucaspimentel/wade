using Wade.LnkParser;

namespace Wade.Preview;

internal sealed class ShortcutMetadataProvider : IMetadataProvider
{
    public string Label => "Shortcut properties";

    public bool CanProvideMetadata(string path, PreviewContext context) =>
        Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase);

    public MetadataResult? GetMetadata(string path, PreviewContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            LnkFile lnk = LnkFile.Parse(path);
            var sections = new List<MetadataSection>();

            // Shortcut section
            var entries = new List<MetadataEntry>();
            string? target = lnk.GetTargetPath();

            if (target != null)
            {
                entries.Add(new MetadataEntry("Target", target));
            }

            string? launchUri = lnk.GetLaunchUri();

            if (launchUri != null)
            {
                entries.Add(new MetadataEntry("Launch URI", launchUri));
            }

            if (lnk.StringData?.WorkingDir is { Length: > 0 } workingDir)
            {
                entries.Add(new MetadataEntry("Working Dir", workingDir));
            }

            if (lnk.StringData?.CommandLineArguments is { Length: > 0 } args)
            {
                entries.Add(new MetadataEntry("Arguments", args));
            }

            if (lnk.StringData?.Name is { Length: > 0 } description)
            {
                entries.Add(new MetadataEntry("Description", description));
            }

            if (lnk.StringData?.IconLocation is { Length: > 0 } iconLocation)
            {
                entries.Add(new MetadataEntry("Icon", iconLocation));
            }

            if (lnk.Header.HotKey != 0)
            {
                entries.Add(new MetadataEntry("Hotkey", HotKeyHelper.Decode(lnk.Header.HotKey)));
            }

            if (lnk.Header.ShowCommand != ShowCommand.Normal)
            {
                entries.Add(new MetadataEntry("Window", lnk.Header.ShowCommand.ToString()));
            }

            if (entries.Count > 0)
            {
                sections.Add(new MetadataSection("Shortcut", entries.ToArray()));
            }

            // Link info section
            if (lnk.LinkInfo != null)
            {
                var linkEntries = new List<MetadataEntry>();

                if (lnk.LinkInfo.LocalBasePath is { Length: > 0 } localPath)
                {
                    linkEntries.Add(new MetadataEntry("Local Path", localPath));
                }

                if (lnk.LinkInfo.VolumeId?.VolumeLabel is { Length: > 0 } volLabel)
                {
                    linkEntries.Add(new MetadataEntry("Volume", volLabel));
                }

                if (linkEntries.Count > 0)
                {
                    sections.Add(new MetadataSection("Link Info", linkEntries.ToArray()));
                }
            }

            return new MetadataResult
            {
                Sections = sections.ToArray(),
                FileTypeLabel = "Windows Shortcut",
            };
        }
        catch
        {
            return null;
        }
    }
}
