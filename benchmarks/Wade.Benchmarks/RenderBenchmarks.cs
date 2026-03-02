using System.Text;
using BenchmarkDotNet.Attributes;
using Wade.FileSystem;
using Wade.Terminal;
using Wade.UI;

namespace Wade.Benchmarks;

[MemoryDiagnoser]
public class RenderBenchmarks
{
    private static readonly FileSystemEntry CsFile =
        new("Program.cs", @"C:\src\Program.cs", IsDirectory: false, Size: 1024, LastModified: default);

    private static readonly FileSystemEntry UnknownFile =
        new("binary.dat", @"C:\src\binary.dat", IsDirectory: false, Size: 512, LastModified: default);

    private static readonly FileSystemEntry Directory =
        new("src", @"C:\src", IsDirectory: true, Size: 0, LastModified: default);

    private ScreenBuffer _buffer = null!;
    private List<FileSystemEntry> _entries = null!;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new ScreenBuffer(120, 40);
        _entries = [CsFile, UnknownFile, Directory];
    }

    [Benchmark]
    public Rune GetIcon_KnownExtension() => FileIcons.GetIcon(CsFile);

    [Benchmark]
    public Rune GetIcon_UnknownExtension() => FileIcons.GetIcon(UnknownFile);

    [Benchmark]
    public Rune GetIcon_Directory() => FileIcons.GetIcon(Directory);

    [Benchmark]
    public string IconNameConcat()
    {
        var icon = FileIcons.GetIcon(CsFile);
        return icon.ToString() + " " + CsFile.Name;
    }

    [Benchmark]
    public void WriteString_Ascii()
    {
        var style = new CellStyle(null, null);
        _buffer.WriteString(0, 0, "Program.cs", style, 20);
    }

    [Benchmark]
    public void WriteString_WithIcon()
    {
        var icon = FileIcons.GetIcon(CsFile);
        var display = icon.ToString() + " " + CsFile.Name;
        var style = new CellStyle(null, null);
        _buffer.WriteString(0, 0, display, style, 20);
    }

    [Benchmark]
    public void RenderFileList_WithIcons()
    {
        _buffer.Clear();
        PaneRenderer.RenderFileList(
            _buffer,
            pane: new Rect(0, 0, 40, 20),
            _entries,
            selectedIndex: 0,
            scrollOffset: 0,
            isActive: true,
            showIcons: true);
    }

    [Benchmark]
    public void RenderFileList_NoIcons()
    {
        _buffer.Clear();
        PaneRenderer.RenderFileList(
            _buffer,
            pane: new Rect(0, 0, 40, 20),
            _entries,
            selectedIndex: 0,
            scrollOffset: 0,
            isActive: true,
            showIcons: false);
    }
}
