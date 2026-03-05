using System.Text;
using System.Text.RegularExpressions;
using Wade.Terminal;
using Wade.UI;

namespace Wade.Tests;

public class ModalInputTests
{
    // We test the handler logic via a lightweight test harness that mirrors App's modal state.

    // ── Confirm dialog ──────────────────────────────────────────────────────

    [Fact]
    public void ConfirmDialog_YKey_InvokesActionAndReturnsToNormal()
    {
        var harness = new ModalHarness();
        bool invoked = false;
        harness.ShowConfirm("Delete?", "Are you sure?", () => invoked = true);

        harness.HandleKey(new KeyEvent(ConsoleKey.Y, 'y', false, false, false));

        Assert.True(invoked);
        Assert.Equal(InputMode.Normal, harness.Mode);
    }

    [Theory]
    [InlineData(ConsoleKey.N, 'n')]
    [InlineData(ConsoleKey.Escape, '\0')]
    public void ConfirmDialog_NOrEscape_DismissesWithoutAction(ConsoleKey key, char keyChar)
    {
        var harness = new ModalHarness();
        bool invoked = false;
        harness.ShowConfirm("Delete?", "Are you sure?", () => invoked = true);

        harness.HandleKey(new KeyEvent(key, keyChar, false, false, false));

        Assert.False(invoked);
        Assert.Equal(InputMode.Normal, harness.Mode);
    }

    [Theory]
    [InlineData(ConsoleKey.A, 'a')]
    [InlineData(ConsoleKey.Enter, '\r')]
    [InlineData(ConsoleKey.J, 'j')]
    public void ConfirmDialog_OtherKeys_ConsumedButIgnored(ConsoleKey key, char keyChar)
    {
        var harness = new ModalHarness();
        bool invoked = false;
        harness.ShowConfirm("Delete?", "Are you sure?", () => invoked = true);

        harness.HandleKey(new KeyEvent(key, keyChar, false, false, false));

        Assert.False(invoked);
        Assert.Equal(InputMode.Confirm, harness.Mode);
    }

    // ── Text input dialog ───────────────────────────────────────────────────

    [Fact]
    public void TextInput_PrintableChars_ForwardedToTextInput()
    {
        var harness = new ModalHarness();
        harness.ShowTextInput("Rename", "", _ => { });

        harness.HandleKey(new KeyEvent(ConsoleKey.A, 'a', false, false, false));
        harness.HandleKey(new KeyEvent(ConsoleKey.B, 'b', false, false, false));
        harness.HandleKey(new KeyEvent(ConsoleKey.C, 'c', false, false, false));

        Assert.Equal("abc", harness.TextInputValue);
        Assert.Equal(InputMode.TextInput, harness.Mode);
    }

    [Fact]
    public void TextInput_Backspace_DeletesBackward()
    {
        var harness = new ModalHarness();
        harness.ShowTextInput("Rename", "abc", _ => { });

        harness.HandleKey(new KeyEvent(ConsoleKey.Backspace, '\b', false, false, false));

        Assert.Equal("ab", harness.TextInputValue);
    }

    [Fact]
    public void TextInput_Delete_DeletesForward()
    {
        var harness = new ModalHarness();
        harness.ShowTextInput("Rename", "abc", _ => { });
        // Move cursor to beginning
        harness.HandleKey(new KeyEvent(ConsoleKey.Home, '\0', false, false, false));

        harness.HandleKey(new KeyEvent(ConsoleKey.Delete, '\0', false, false, false));

        Assert.Equal("bc", harness.TextInputValue);
    }

    [Fact]
    public void TextInput_ArrowKeys_MoveCursor()
    {
        var harness = new ModalHarness();
        harness.ShowTextInput("Rename", "abc", _ => { });

        harness.HandleKey(new KeyEvent(ConsoleKey.LeftArrow, '\0', false, false, false));

        Assert.Equal(2, harness.TextInputCursorPosition);

        harness.HandleKey(new KeyEvent(ConsoleKey.RightArrow, '\0', false, false, false));

        Assert.Equal(3, harness.TextInputCursorPosition);
    }

    [Fact]
    public void TextInput_HomeEnd_MoveCursor()
    {
        var harness = new ModalHarness();
        harness.ShowTextInput("Rename", "abc", _ => { });

        harness.HandleKey(new KeyEvent(ConsoleKey.Home, '\0', false, false, false));
        Assert.Equal(0, harness.TextInputCursorPosition);

        harness.HandleKey(new KeyEvent(ConsoleKey.End, '\0', false, false, false));
        Assert.Equal(3, harness.TextInputCursorPosition);
    }

    [Fact]
    public void TextInput_Enter_InvokesActionWithValueAndReturnsToNormal()
    {
        var harness = new ModalHarness();
        string? result = null;
        harness.ShowTextInput("Rename", "hello", v => result = v);

        // Append " world"
        foreach (char c in " world")
            harness.HandleKey(new KeyEvent(ConsoleKey.None, c, false, false, false));

        harness.HandleKey(new KeyEvent(ConsoleKey.Enter, '\r', false, false, false));

        Assert.Equal("hello world", result);
        Assert.Equal(InputMode.Normal, harness.Mode);
    }

    [Fact]
    public void TextInput_Escape_DismissesWithoutAction()
    {
        var harness = new ModalHarness();
        string? result = null;
        harness.ShowTextInput("Rename", "hello", v => result = v);

        harness.HandleKey(new KeyEvent(ConsoleKey.Escape, '\x1b', false, false, false));

        Assert.Null(result);
        Assert.Equal(InputMode.Normal, harness.Mode);
    }

    // ── Mode isolation ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(ConsoleKey.J, 'j')]
    [InlineData(ConsoleKey.K, 'k')]
    [InlineData(ConsoleKey.Q, 'q')]
    [InlineData(ConsoleKey.UpArrow, '\0')]
    [InlineData(ConsoleKey.DownArrow, '\0')]
    public void ConfirmMode_NormalNavigationKeys_DoNotLeakThrough(ConsoleKey key, char keyChar)
    {
        var harness = new ModalHarness();
        harness.ShowConfirm("Test", "Test?", () => { });

        // These keys should be consumed (not crash, not change mode unless Y/N/Esc)
        harness.HandleKey(new KeyEvent(key, keyChar, false, false, false));

        Assert.Equal(InputMode.Confirm, harness.Mode);
    }

    [Fact]
    public void TextInputMode_EscapeKey_DoesNotQuitApp()
    {
        var harness = new ModalHarness();
        harness.ShowTextInput("Test", "", _ => { });

        harness.HandleKey(new KeyEvent(ConsoleKey.Escape, '\x1b', false, false, false));

        // Should return to Normal, not trigger quit
        Assert.Equal(InputMode.Normal, harness.Mode);
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    [Fact]
    public void ConfirmDialog_Renders_MessageAndFooter()
    {
        var buf = new ScreenBuffer(80, 24);
        string message = "Are you sure?";
        string footer = "[Y] Yes  [N] No";

        int contentWidth = Math.Max(message.Length, footer.Length) + 2;
        Rect content = DialogBox.Render(buf, 80, 24, contentWidth, 1, title: "Delete", footer: footer);

        var textStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        int msgCol = content.Left + (content.Width - message.Length) / 2;
        buf.WriteString(content.Top, msgCol, message, textStyle);

        string output = StripAnsi(Flush(buf));
        Assert.Contains("Delete", output);
        Assert.Contains("Are you sure?", output);
        Assert.Contains("[Y] Yes", output);
        Assert.Contains("[N] No", output);
    }

    [Fact]
    public void TextInputDialog_Renders_TitleAndFooter()
    {
        var buf = new ScreenBuffer(80, 24);
        string footer = "[Enter] Confirm  [Esc] Cancel";

        Rect content = DialogBox.Render(buf, 80, 24, 40, 1, title: "Rename", footer: footer);

        var inputStyle = new CellStyle(new Color(200, 200, 200), DialogBox.BgColor);
        var textInput = new TextInput("test.txt");
        textInput.Render(buf, content.Top, content.Left, content.Width, inputStyle);

        string output = StripAnsi(Flush(buf));
        Assert.Contains("Rename", output);
        Assert.Contains("test.txt", output);
        Assert.Contains("[Enter] Confirm", output);
        Assert.Contains("[Esc] Cancel", output);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string Flush(ScreenBuffer buf)
    {
        var sb = new StringBuilder();
        buf.Flush(sb);
        return sb.ToString();
    }

    private static string StripAnsi(string s) =>
        Regex.Replace(s, @"\x1b\[[^a-zA-Z]*[a-zA-Z]", "");

    /// <summary>
    /// Lightweight test harness that mirrors App's modal state and handler logic
    /// without requiring the full App run loop.
    /// </summary>
    private sealed class ModalHarness
    {
        private InputMode _inputMode = InputMode.Normal;

        private string? _confirmTitle;
        private string? _confirmMessage;
        private Action? _confirmYesAction;

        private TextInput? _activeTextInput;
        private string? _textInputTitle;
        private Action<string>? _textInputCompleteAction;

        public InputMode Mode => _inputMode;
        public string? TextInputValue => _activeTextInput?.Value;
        public int TextInputCursorPosition => _activeTextInput?.CursorPosition ?? -1;

        public void ShowConfirm(string title, string message, Action onYes)
        {
            _inputMode = InputMode.Confirm;
            _confirmTitle = title;
            _confirmMessage = message;
            _confirmYesAction = onYes;
        }

        public void ShowTextInput(string title, string initialValue, Action<string> onComplete)
        {
            _inputMode = InputMode.TextInput;
            _textInputTitle = title;
            _activeTextInput = new TextInput(initialValue);
            _textInputCompleteAction = onComplete;
        }

        public void HandleKey(KeyEvent key)
        {
            switch (_inputMode)
            {
                case InputMode.TextInput:
                    HandleTextInputKey(key);
                    break;
                case InputMode.Confirm:
                    HandleConfirmKey(key);
                    break;
            }
        }

        private void HandleTextInputKey(KeyEvent key)
        {
            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    _inputMode = InputMode.Normal;
                    _activeTextInput = null;
                    _textInputTitle = null;
                    _textInputCompleteAction = null;
                    break;

                case ConsoleKey.Enter:
                    string value = _activeTextInput!.Value;
                    Action<string>? completeAction = _textInputCompleteAction;
                    _inputMode = InputMode.Normal;
                    _activeTextInput = null;
                    _textInputTitle = null;
                    _textInputCompleteAction = null;
                    completeAction?.Invoke(value);
                    break;

                case ConsoleKey.Backspace:
                    _activeTextInput!.DeleteBackward();
                    break;

                case ConsoleKey.Delete:
                    _activeTextInput!.DeleteForward();
                    break;

                case ConsoleKey.LeftArrow:
                    _activeTextInput!.MoveCursorLeft();
                    break;

                case ConsoleKey.RightArrow:
                    _activeTextInput!.MoveCursorRight();
                    break;

                case ConsoleKey.Home:
                    _activeTextInput!.MoveCursorHome();
                    break;

                case ConsoleKey.End:
                    _activeTextInput!.MoveCursorEnd();
                    break;

                default:
                    if (key.KeyChar >= ' ')
                        _activeTextInput!.InsertChar(key.KeyChar);
                    break;
            }
        }

        private void HandleConfirmKey(KeyEvent key)
        {
            switch (key.Key)
            {
                case ConsoleKey.Y:
                    Action? yesAction = _confirmYesAction;
                    _inputMode = InputMode.Normal;
                    _confirmTitle = null;
                    _confirmMessage = null;
                    _confirmYesAction = null;
                    yesAction?.Invoke();
                    break;

                case ConsoleKey.N:
                case ConsoleKey.Escape:
                    _inputMode = InputMode.Normal;
                    _confirmTitle = null;
                    _confirmMessage = null;
                    _confirmYesAction = null;
                    break;
            }
        }
    }
}
