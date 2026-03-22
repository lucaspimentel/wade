using System.Collections.Frozen;

namespace Wade.Highlighting.Languages;

internal sealed class ShellLanguage : RegexLanguage
{
    protected override FrozenSet<string> Keywords { get; } = new[]
    {
        "if", "then", "else", "elif", "fi", "for", "while", "do", "done",
        "case", "esac", "in", "function", "return", "exit", "export", "local",
        "readonly", "declare", "typeset", "unset", "shift", "source",
        "break", "continue", "trap", "exec",
    }.ToFrozenSet();

    protected override FrozenSet<string> Constants { get; } = new[]
    {
        "true", "false",
    }.ToFrozenSet();

    protected override FrozenSet<string> Builtins { get; } = new[]
    {
        "echo", "printf", "read", "test", "cd", "pwd", "ls", "cp", "mv",
        "rm", "mkdir", "chmod", "chown", "grep", "sed", "awk", "find",
        "cat", "head", "tail", "sort", "uniq", "wc", "cut", "tr",
    }.ToFrozenSet();

    protected override string? LineCommentPrefix => "#";

    protected override (string Open, string Close)? BlockComment => null;

    protected override bool TryMatchString(string line, int pos, List<StyledSpan> spans, ref byte state, out int end)
    {
        // Single-quoted strings: no escape processing
        if (line[pos] == '\'')
        {
            int p = pos + 1;
            while (p < line.Length && line[p] != '\'')
            {
                p++;
            }

            if (p < line.Length)
            {
                p++;
            }

            spans.Add(new StyledSpan(pos, p - pos, TokenKind.String));
            end = p;
            return true;
        }

        return base.TryMatchString(line, pos, spans, ref state, out end);
    }
}
