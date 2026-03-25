using Wade.FileSystem;

namespace Wade.UI;

internal sealed class ConfigDialogState
{
    public bool ShowIcons { get; set; }

    public bool ImagePreviews { get; set; }

    public bool ShowHiddenFiles { get; set; }

    public bool ShowSystemFiles { get; set; }

    public SortMode SortMode { get; set; }

    public bool SortAscending { get; set; }

    public bool ConfirmDelete { get; set; }

    public bool PreviewPane { get; set; }

    public bool SizeColumn { get; set; }

    public bool DateColumn { get; set; }

    public bool ZipPreview { get; set; }

    public bool PdfPreview { get; set; }

    public bool PdfMetadata { get; set; }

    public bool MarkdownPreview { get; set; }

    public bool GlowPreview { get; set; }

    public bool Ffprobe { get; set; }

    public bool Mediainfo { get; set; }

    public bool CopySymlinksAsLinks { get; set; }

    public bool TerminalTitle { get; set; }

    public bool GitStatus { get; set; }

    public bool FileMetadata { get; set; }

    public bool FilePreviews { get; set; }

    public bool ArchiveMetadata { get; set; }

    public bool DirSizeSsd { get; set; }

    public bool DirSizeHdd { get; set; }

    public bool DirSizeNetwork { get; set; }

    public List<ConfigItem> Items { get; private set; } = [];

    public int SelectedIndex { get; set; }

    public static ConfigDialogState FromConfig(WadeConfig config)
    {
        var state = new ConfigDialogState
        {
            ShowIcons = config.ShowIconsEnabled,
            ImagePreviews = config.ImagePreviewsEnabled,
            ShowHiddenFiles = config.ShowHiddenFiles,
            ShowSystemFiles = config.ShowSystemFiles,
            SortMode = config.SortMode,
            SortAscending = config.SortAscending,
            ConfirmDelete = config.ConfirmDeleteEnabled,
            PreviewPane = config.PreviewPaneEnabled,
            SizeColumn = config.SizeColumnEnabled,
            DateColumn = config.DateColumnEnabled,
            ZipPreview = config.ZipPreviewEnabled,
            PdfPreview = config.PdfPreviewEnabled,
            PdfMetadata = config.PdfMetadataEnabled,
            MarkdownPreview = config.MarkdownPreviewEnabled,
            GlowPreview = config.GlowPreviewEnabled,
            Ffprobe = config.FfprobeEnabled,
            Mediainfo = config.MediainfoEnabled,
            CopySymlinksAsLinks = config.CopySymlinksAsLinksEnabled,
            TerminalTitle = config.TerminalTitleEnabled,
            GitStatus = config.GitStatusEnabled,
            FileMetadata = config.FileMetadataEnabled,
            FilePreviews = config.FilePreviewsEnabled,
            ArchiveMetadata = config.ArchiveMetadataEnabled,
            DirSizeSsd = config.DirSizeSsdEnabled,
            DirSizeHdd = config.DirSizeHddEnabled,
            DirSizeNetwork = config.DirSizeNetworkEnabled,
        };

        state.BuildItems();
        return state;
    }

    public void ApplyTo(WadeConfig config)
    {
        config.ShowIconsEnabled = ShowIcons;
        config.ImagePreviewsEnabled = ImagePreviews;
        config.ShowHiddenFiles = ShowHiddenFiles;
        config.ShowSystemFiles = ShowHiddenFiles && ShowSystemFiles;
        ShowSystemFiles = config.ShowSystemFiles;
        config.SortMode = SortMode;
        config.SortAscending = SortAscending;
        config.ConfirmDeleteEnabled = ConfirmDelete;
        config.PreviewPaneEnabled = PreviewPane;
        config.SizeColumnEnabled = SizeColumn;
        config.DateColumnEnabled = DateColumn;
        config.ZipPreviewEnabled = ZipPreview;
        config.PdfPreviewEnabled = PdfPreview;
        config.PdfMetadataEnabled = PdfMetadata;
        config.MarkdownPreviewEnabled = MarkdownPreview;
        config.GlowPreviewEnabled = GlowPreview;
        config.FfprobeEnabled = Ffprobe;
        config.MediainfoEnabled = Mediainfo;
        config.CopySymlinksAsLinksEnabled = CopySymlinksAsLinks;
        config.TerminalTitleEnabled = TerminalTitle;
        config.GitStatusEnabled = GitStatus;
        config.FileMetadataEnabled = FileMetadata;
        config.FilePreviewsEnabled = FilePreviews;
        config.ArchiveMetadataEnabled = ArchiveMetadata;
        config.DirSizeSsdEnabled = DirSizeSsd;
        config.DirSizeHddEnabled = DirSizeHdd;
        config.DirSizeNetworkEnabled = DirSizeNetwork;
    }

    public void MoveUp()
    {
        int prev = SelectedIndex - 1;
        while (prev >= 0 && !Items[prev].IsEnabled)
        {
            prev--;
        }

        if (prev >= 0)
        {
            SelectedIndex = prev;
        }
        else
        {
            int last = Items.Count - 1;
            while (last > SelectedIndex && !Items[last].IsEnabled)
            {
                last--;
            }

            if (last > SelectedIndex)
            {
                SelectedIndex = last;
            }
        }
    }

    public void MoveDown()
    {
        int maxIndex = Items.Count - 1;
        int next = SelectedIndex + 1;
        while (next <= maxIndex && !Items[next].IsEnabled)
        {
            next++;
        }

        if (next <= maxIndex)
        {
            SelectedIndex = next;
        }
        else
        {
            int first = 0;
            while (first < SelectedIndex && !Items[first].IsEnabled)
            {
                first++;
            }

            if (first < SelectedIndex)
            {
                SelectedIndex = first;
            }
        }
    }

    public void ToggleSelected() => Items[SelectedIndex].Toggle();

    public void CycleNextSelected() => Items[SelectedIndex].CycleNext?.Invoke();

    public void CyclePrevSelected() => Items[SelectedIndex].CyclePrev?.Invoke();

    internal void BuildItems()
    {
        Items =
        [
            new ConfigItem
            {
                Label = "Show Icons",
                FormatValue = () => FormatBool(ShowIcons),
                Toggle = () => ShowIcons = !ShowIcons,
            },
            new ConfigItem
            {
                Label = "Show Hidden Files",
                FormatValue = () => FormatBool(ShowHiddenFiles),
                Toggle = () => ShowHiddenFiles = !ShowHiddenFiles,
            },
        ];

        if (OperatingSystem.IsWindows())
        {
            Items.Add(new ConfigItem
            {
                Label = "Show System Files",
                Indent = 1,
                FormatValue = () => FormatBool(ShowSystemFiles),
                Toggle = () => ShowSystemFiles = !ShowSystemFiles,
                EnabledWhen = () => ShowHiddenFiles,
            });
        }

        Items.AddRange([
            new ConfigItem
            {
                Label = "Sort Mode",
                FormatValue = () => $"\u25c4 {SortMode.ToString().ToLowerInvariant()} \u25ba",
                Toggle = () => SortMode = CycleSortModeNext(SortMode),
                CycleNext = () => SortMode = CycleSortModeNext(SortMode),
                CyclePrev = () => SortMode = CycleSortModePrev(SortMode),
            },
            new ConfigItem
            {
                Label = "Sort Ascending",
                FormatValue = () => FormatBool(SortAscending),
                Toggle = () => SortAscending = !SortAscending,
            },
            new ConfigItem
            {
                Label = "Show Size Column",
                FormatValue = () => FormatBool(SizeColumn),
                Toggle = () => SizeColumn = !SizeColumn,
            },
            new ConfigItem
            {
                Label = "Directory Sizes on SSD",
                Indent = 1,
                FormatValue = () => FormatBool(DirSizeSsd),
                Toggle = () => DirSizeSsd = !DirSizeSsd,
                EnabledWhen = () => SizeColumn,
            },
            new ConfigItem
            {
                Label = "Directory Sizes on HDD",
                Indent = 1,
                FormatValue = () => FormatBool(DirSizeHdd),
                Toggle = () => DirSizeHdd = !DirSizeHdd,
                EnabledWhen = () => SizeColumn,
            },
            new ConfigItem
            {
                Label = "Directory Sizes on Network",
                Indent = 1,
                FormatValue = () => FormatBool(DirSizeNetwork),
                Toggle = () => DirSizeNetwork = !DirSizeNetwork,
                EnabledWhen = () => SizeColumn,
            },
            new ConfigItem
            {
                Label = "Show Date Column",
                FormatValue = () => FormatBool(DateColumn),
                Toggle = () => DateColumn = !DateColumn,
            },
            new ConfigItem
            {
                Label = "Confirm Delete",
                FormatValue = () => FormatBool(ConfirmDelete),
                Toggle = () => ConfirmDelete = !ConfirmDelete,
            },
            new ConfigItem
            {
                Label = "Copy Symlinks as Link",
                FormatValue = () => FormatBool(CopySymlinksAsLinks),
                Toggle = () => CopySymlinksAsLinks = !CopySymlinksAsLinks,
            },
            new ConfigItem
            {
                Label = "Change Terminal Title",
                FormatValue = () => FormatBool(TerminalTitle),
                Toggle = () => TerminalTitle = !TerminalTitle,
            },
            new ConfigItem
            {
                Label = "Show Git Status",
                FormatValue = () => FormatBool(GitStatus),
                Toggle = () => GitStatus = !GitStatus,
            },
            new ConfigItem
            {
                Label = "Show Right Pane",
                FormatValue = () => FormatBool(PreviewPane),
                Toggle = () => PreviewPane = !PreviewPane,
            },
            new ConfigItem
            {
                Label = "Show File Details",
                Indent = 1,
                FormatValue = () => FormatBool(FileMetadata),
                Toggle = () => FileMetadata = !FileMetadata,
                EnabledWhen = () => PreviewPane,
            },
            new ConfigItem
            {
                Label = "Show Archive Details",
                Indent = 2,
                FormatValue = () => FormatBool(ArchiveMetadata),
                Toggle = () => ArchiveMetadata = !ArchiveMetadata,
                EnabledWhen = () => PreviewPane && FileMetadata,
            },
            new ConfigItem
            {
                Label = "Show PDF Details (pdfinfo)",
                Indent = 2,
                FormatValue = () => FormatBool(PdfMetadata),
                Toggle = () => PdfMetadata = !PdfMetadata,
                EnabledWhen = () => PreviewPane && FileMetadata,
            },
            new ConfigItem
            {
                Label = "Show Media Details (ffprobe)",
                Indent = 2,
                FormatValue = () => FormatBool(Ffprobe),
                Toggle = () => Ffprobe = !Ffprobe,
                EnabledWhen = () => PreviewPane && FileMetadata,
            },
            new ConfigItem
            {
                Label = "Show Media Details (mediainfo)",
                Indent = 2,
                FormatValue = () => FormatBool(Mediainfo),
                Toggle = () => Mediainfo = !Mediainfo,
                EnabledWhen = () => PreviewPane && FileMetadata,
            },
            new ConfigItem
            {
                Label = "Show File Previews",
                Indent = 1,
                FormatValue = () => FormatBool(FilePreviews),
                Toggle = () => FilePreviews = !FilePreviews,
                EnabledWhen = () => PreviewPane,
            },
            new ConfigItem
            {
                Label = "Show Image Previews",
                Indent = 2,
                FormatValue = () => FormatBool(ImagePreviews),
                Toggle = () => ImagePreviews = !ImagePreviews,
                EnabledWhen = () => PreviewPane && FilePreviews,
            },
            new ConfigItem
            {
                Label = "Show PDF Previews (pdftopng)",
                Indent = 2,
                FormatValue = () => FormatBool(PdfPreview),
                Toggle = () => PdfPreview = !PdfPreview,
                EnabledWhen = () => PreviewPane && FilePreviews,
            },
            new ConfigItem
            {
                Label = "Show Archive Contents",
                Indent = 2,
                FormatValue = () => FormatBool(ZipPreview),
                Toggle = () => ZipPreview = !ZipPreview,
                EnabledWhen = () => PreviewPane && FilePreviews,
            },
            new ConfigItem
            {
                Label = "Show Markdown Preview (built-in)",
                Indent = 2,
                FormatValue = () => FormatBool(MarkdownPreview),
                Toggle = () => MarkdownPreview = !MarkdownPreview,
                EnabledWhen = () => PreviewPane && FilePreviews,
            },
            new ConfigItem
            {
                Label = "Show Markdown Preview (glow)",
                Indent = 2,
                FormatValue = () => FormatBool(GlowPreview),
                Toggle = () => GlowPreview = !GlowPreview,
                EnabledWhen = () => PreviewPane && FilePreviews,
            },
        ]);
    }

    internal static string FormatBool(bool value) => value ? "[X]" : "[ ]";

    private static SortMode CycleSortModeNext(SortMode current) =>
        current switch
        {
            SortMode.Name => SortMode.Modified,
            SortMode.Modified => SortMode.Size,
            SortMode.Size => SortMode.Extension,
            _ => SortMode.Name,
        };

    private static SortMode CycleSortModePrev(SortMode current) =>
        current switch
        {
            SortMode.Name => SortMode.Extension,
            SortMode.Modified => SortMode.Name,
            SortMode.Size => SortMode.Modified,
            _ => SortMode.Size,
        };
}
