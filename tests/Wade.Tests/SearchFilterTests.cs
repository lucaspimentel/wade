using System.Text;
using System.Text.RegularExpressions;
using Wade.FileSystem;
using Wade.Terminal;
using Wade.UI;

namespace Wade.Tests;

public class SearchFilterTests
{
    // ── Entering search ──────────────────────────────────────────────────────

    [Fact]
    public void StartSearch_SetsSearchModeAndCreatesTextInput()
    {
        var harness = new SearchHarness(["file1.txt", "file2.txt"]);

        harness.StartSearch();

        Assert.Equal(InputMode.Search, harness.Mode);
        Assert.NotNull(harness.SearchInputValue);
    }

    // ── Typing filters entries ───────────────────────────────────────────────

    [Fact]
    public void TypingQuery_FiltersEntries()
    {
        var harness = new SearchHarness(["foo.txt", "bar.txt", "foobar.txt"]);
        harness.StartSearch();

        TypeString(harness, "foo");

        Assert.Equal("foo", harness.SearchFilter);
        List<FileSystemEntry> visible = harness.GetVisibleEntries();
        Assert.Equal(2, visible.Count);
        Assert.All(visible, e => Assert.Contains("foo", e.Name, StringComparison.OrdinalIgnoreCase));
    }

    // ── Case-insensitive match ───────────────────────────────────────────────

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        var harness = new SearchHarness(["Foo.txt", "bar.txt"]);
        harness.StartSearch();

        TypeString(harness, "foo");

        List<FileSystemEntry> visible = harness.GetVisibleEntries();
        Assert.Single(visible);
        Assert.Equal("Foo.txt", visible[0].Name);
    }

    // ── Enter persists filter ────────────────────────────────────────────────

    [Fact]
    public void Enter_PersistsFilterAndReturnsToNormal()
    {
        var harness = new SearchHarness(["foo.txt", "bar.txt"]);
        harness.StartSearch();
        TypeString(harness, "foo");

        harness.HandleSearchKey(new KeyEvent(ConsoleKey.Enter, '\r', false, false, false));

        Assert.Equal(InputMode.Normal, harness.Mode);
        Assert.Equal("foo", harness.SearchFilter);
        Assert.Single(harness.GetVisibleEntries());
    }

    // ── Escape clears filter ─────────────────────────────────────────────────

    [Fact]
    public void Escape_ClearsFilterAndReturnsToNormal()
    {
        var harness = new SearchHarness(["foo.txt", "bar.txt"]);
        harness.StartSearch();
        TypeString(harness, "foo");

        harness.HandleSearchKey(new KeyEvent(ConsoleKey.Escape, '\x1b', false, false, false));

        Assert.Equal(InputMode.Normal, harness.Mode);
        Assert.Equal("", harness.SearchFilter);
        Assert.Equal(2, harness.GetVisibleEntries().Count);
    }

    // ── Up/Down navigate filtered list ───────────────────────────────────────

    [Fact]
    public void UpDown_NavigateFilteredList()
    {
        var harness = new SearchHarness(["a.txt", "ab.txt", "abc.txt", "xyz.txt"]);
        harness.StartSearch();
        TypeString(harness, "a");

        // 3 entries match: a.txt, ab.txt, abc.txt — selectedIndex starts at 0
        Assert.Equal(0, harness.SelectedIndex);

        harness.HandleSearchKey(new KeyEvent(ConsoleKey.DownArrow, '\0', false, false, false));
        Assert.Equal(1, harness.SelectedIndex);

        harness.HandleSearchKey(new KeyEvent(ConsoleKey.DownArrow, '\0', false, false, false));
        Assert.Equal(2, harness.SelectedIndex);

        harness.HandleSearchKey(new KeyEvent(ConsoleKey.UpArrow, '\0', false, false, false));
        Assert.Equal(1, harness.SelectedIndex);
    }

    // ── Empty filter shows all entries ────────────────────────────────────────

    [Fact]
    public void BackspacingToEmpty_ShowsAllEntries()
    {
        var harness = new SearchHarness(["foo.txt", "bar.txt"]);
        harness.StartSearch();
        TypeString(harness, "z");
        Assert.Empty(harness.GetVisibleEntries());

        harness.HandleSearchKey(new KeyEvent(ConsoleKey.Backspace, '\b', false, false, false));

        Assert.Equal("", harness.SearchFilter);
        Assert.Equal(2, harness.GetVisibleEntries().Count);
    }

    // ── Navigation keys don't leak ───────────────────────────────────────────

    [Theory]
    [InlineData(ConsoleKey.J, 'j')]
    [InlineData(ConsoleKey.K, 'k')]
    [InlineData(ConsoleKey.Q, 'q')]
    public void VimAndQuitKeys_InsertedAsChars_NotNavigation(ConsoleKey key, char keyChar)
    {
        var harness = new SearchHarness(["file.txt"]);
        harness.StartSearch();

        harness.HandleSearchKey(new KeyEvent(key, keyChar, false, false, false));

        Assert.Equal(InputMode.Search, harness.Mode);
        Assert.Equal(keyChar.ToString(), harness.SearchFilter);
    }

    // ── Directory change clears filter ───────────────────────────────────────

    [Fact]
    public void ClearSearchFilter_ResetsAllState()
    {
        var harness = new SearchHarness(["foo.txt", "bar.txt"]);
        harness.StartSearch();
        TypeString(harness, "foo");

        harness.ClearSearchFilter();

        Assert.Equal(InputMode.Normal, harness.Mode);
        Assert.Equal("", harness.SearchFilter);
        Assert.Equal(2, harness.GetVisibleEntries().Count);
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    [Fact]
    public void SearchBar_RendersSlashPrefixAndFilterText()
    {
        var buf = new ScreenBuffer(80, 24);

        // Simulate rendering a search bar at row 20
        int row = 20;
        int col = 0;
        string filter = "test";

        var labelStyle = new CellStyle(new Color(220, 220, 100), null);
        buf.Put(row, col, '/', labelStyle);

        var textStyle = new CellStyle(new Color(200, 200, 200), null);
        buf.WriteString(row, col + 1, filter, textStyle, 79);

        string output = StripAnsi(Flush(buf));
        Assert.Contains("/test", output);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void TypeString(SearchHarness harness, string text)
    {
        foreach (char c in text)
        {
            harness.HandleSearchKey(new KeyEvent(ConsoleKey.None, c, false, false, false));
        }
    }

    private static string Flush(ScreenBuffer buf)
    {
        var sb = new StringBuilder();
        buf.Flush(sb);
        return sb.ToString();
    }

    private static string StripAnsi(string s) =>
        Regex.Replace(s, @"\x1b\[[^a-zA-Z]*[a-zA-Z]", "");
    // ── Search harness ───────────────────────────────────────────────────────

    private sealed class SearchHarness
    {
        private readonly List<FileSystemEntry> _allEntries;
        private List<FileSystemEntry>? _filteredEntries;
        private TextInput? _searchInput;

        public int SelectedIndex;

        public SearchHarness(IEnumerable<string> fileNames)
        {
            _allEntries = fileNames
                .Select(n => new FileSystemEntry(n, @"C:\" + n, false, 0, DateTime.MinValue, LinkTarget: null, IsBrokenSymlink: false,
                    IsDrive: false))
                .ToList();
        }

        public InputMode Mode { get; private set; } = InputMode.Normal;

        public string SearchFilter { get; private set; } = "";

        public string? SearchInputValue => _searchInput?.Value;

        public List<FileSystemEntry> GetVisibleEntries()
        {
            if (string.IsNullOrEmpty(SearchFilter))
            {
                _filteredEntries = null;
                return _allEntries;
            }

            _filteredEntries ??= _allEntries
                .Where(e => e.Name.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return _filteredEntries;
        }

        public void StartSearch()
        {
            Mode = InputMode.Search;
            _searchInput = new TextInput(SearchFilter);
        }

        public void ClearSearchFilter()
        {
            SearchFilter = "";
            _filteredEntries = null;
            _searchInput = null;
            if (Mode == InputMode.Search)
            {
                Mode = InputMode.Normal;
            }
        }

        public void HandleSearchKey(KeyEvent key)
        {
            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    ClearSearchFilter();
                    SelectedIndex = 0;
                    break;

                case ConsoleKey.Enter:
                    Mode = InputMode.Normal;
                    _searchInput = null;
                    break;

                case ConsoleKey.UpArrow:
                    if (SelectedIndex > 0)
                    {
                        SelectedIndex--;
                    }

                    break;

                case ConsoleKey.DownArrow:
                {
                    List<FileSystemEntry> entries = GetVisibleEntries();
                    if (SelectedIndex < entries.Count - 1)
                    {
                        SelectedIndex++;
                    }

                    break;
                }

                case ConsoleKey.LeftArrow:
                    _searchInput!.MoveCursorLeft();
                    break;

                case ConsoleKey.RightArrow:
                    _searchInput!.MoveCursorRight();
                    break;

                case ConsoleKey.Home:
                    _searchInput!.MoveCursorHome();
                    break;

                case ConsoleKey.End:
                    _searchInput!.MoveCursorEnd();
                    break;

                case ConsoleKey.Backspace:
                    _searchInput!.DeleteBackward();
                    SearchFilter = _searchInput.Value;
                    _filteredEntries = null;
                    SelectedIndex = 0;
                    break;

                case ConsoleKey.Delete:
                    _searchInput!.DeleteForward();
                    SearchFilter = _searchInput.Value;
                    _filteredEntries = null;
                    SelectedIndex = 0;
                    break;

                default:
                    if (key.KeyChar >= ' ')
                    {
                        _searchInput!.InsertChar(key.KeyChar);
                        SearchFilter = _searchInput.Value;
                        _filteredEntries = null;
                        SelectedIndex = 0;
                    }

                    break;
            }
        }
    }
}
