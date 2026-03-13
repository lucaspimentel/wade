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
        new("Program.cs", @"C:\src\Program.cs", IsDirectory: false, Size: 1024, LastModified: default, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

    private static readonly FileSystemEntry UnknownFile =
        new("binary.dat", @"C:\src\binary.dat", IsDirectory: false, Size: 512, LastModified: default, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

    private static readonly FileSystemEntry Directory =
        new("src", @"C:\src", IsDirectory: true, Size: 0, LastModified: default, LinkTarget: null, IsBrokenSymlink: false, IsDrive: false);

    private ScreenBuffer _buffer = null!;
    private StringBuilder _sb = null!;
    private List<FileSystemEntry> _entries = null!;
    private TextWriter _originalOut = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _buffer = new ScreenBuffer(120, 40);
        _sb = new StringBuilder();
        _entries = [CsFile, UnknownFile, Directory];
        _originalOut = Console.Out;
        Console.SetOut(TextWriter.Null);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        Console.SetOut(_originalOut);
    }

    [IterationSetup(Target = nameof(Flush_FullRedraw))]
    public void SetupFlush()
    {
        var style1 = new CellStyle(new Color(100, 149, 237), null);
        var style2 = new CellStyle(null, new Color(40, 40, 40), Bold: true);
        for (int row = 0; row < 5; row++)
        {
            var style = row % 2 == 0 ? style1 : style2;
            _buffer.WriteString(row, 0, "Program.cs  binary.dat  src  README.md  Makefile", style, 120);
        }

        _buffer.ForceFullRedraw();
    }

    [Benchmark]
    public Rune GetIcon_KnownExtension() => FileIcons.GetIcon(CsFile);

    [Benchmark]
    public Rune GetIcon_UnknownExtension() => FileIcons.GetIcon(UnknownFile);

    [Benchmark]
    public Rune GetIcon_Directory() => FileIcons.GetIcon(Directory);

    [Benchmark]
    public void WriteString_Ascii()
    {
        var style = new CellStyle(null, null);
        _buffer.WriteString(0, 0, "Program.cs", style, 20);
    }

    // Mirrors the icon path in RenderFileList: Put(icon), Put(' '), WriteString(name)
    [Benchmark]
    public void WriteEntry_WithIcon()
    {
        var icon = FileIcons.GetIcon(CsFile);
        var style = new CellStyle(null, null);
        _buffer.Put(0, 0, icon, style);
        _buffer.Put(0, 1, ' ', style);
        _buffer.WriteString(0, 2, CsFile.Name, style, 18);
    }

    // Mirrors the no-icon path in RenderFileList: Put(prefix), WriteString(name)
    [Benchmark]
    public void WriteEntry_NoIcon()
    {
        var style = new CellStyle(null, null);
        _buffer.Put(0, 0, ' ', style);
        _buffer.WriteString(0, 1, CsFile.Name, style, 19);
    }

    [Benchmark]
    public void Flush_FullRedraw()
    {
        _buffer.Flush(_sb);
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
