using System.Collections.Frozen;

namespace Wade.Highlighting.Languages;

internal sealed class DockerfileLanguage : ILanguage
{
    private static readonly FrozenSet<string> Instructions = new[]
    {
        "FROM", "RUN", "CMD", "LABEL", "MAINTAINER", "EXPOSE", "ENV", "ADD",
        "COPY", "ENTRYPOINT", "VOLUME", "USER", "WORKDIR", "ARG", "ONBUILD",
        "STOPSIGNAL", "HEALTHCHECK", "SHELL", "CROSS_BUILD",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public StyledLine TokenizeLine(string line, ref byte state)
    {
        if (line.Length == 0)
        {
            return new StyledLine(line, null);
        }

        int pos = 0;

        while (pos < line.Length && line[pos] == ' ')
        {
            pos++;
        }

        if (pos >= line.Length)
        {
            return new StyledLine(line, null);
        }

        // Comment line
        if (line[pos] == '#')
        {
            return new StyledLine(line, [new StyledSpan(pos, line.Length - pos, TokenKind.Comment)]);
        }

        var spans = new List<StyledSpan>();

        // Try to match an instruction keyword at the start of the line
        int wordEnd = pos;
        while (wordEnd < line.Length && IsInstructionChar(line[wordEnd]))
        {
            wordEnd++;
        }

        if (wordEnd > pos)
        {
            string word = line[pos..wordEnd];

            if (Instructions.Contains(word))
            {
                spans.Add(new StyledSpan(pos, wordEnd - pos, TokenKind.Keyword));
            }
        }

        // Scan the rest of the line for strings, variables, and AS keyword
        pos = wordEnd;

        while (pos < line.Length)
        {
            char ch = line[pos];

            // Quoted strings
            if (ch is '"' or '\'')
            {
                int start = pos;
                char quote = ch;
                pos++;

                while (pos < line.Length)
                {
                    if (line[pos] == '\\' && pos + 1 < line.Length)
                    {
                        pos += 2;
                    }
                    else if (line[pos] == quote)
                    {
                        pos++;
                        break;
                    }
                    else
                    {
                        pos++;
                    }
                }

                spans.Add(new StyledSpan(start, pos - start, TokenKind.String));
                continue;
            }

            // Variable references: $VAR or ${VAR}
            if (ch == '$' && pos + 1 < line.Length)
            {
                int start = pos;
                pos++;

                if (pos < line.Length && line[pos] == '{')
                {
                    int close = line.IndexOf('}', pos + 1);

                    if (close >= 0)
                    {
                        pos = close + 1;
                    }
                    else
                    {
                        // Unclosed brace — highlight to end of line
                        pos = line.Length;
                    }
                }
                else
                {
                    while (pos < line.Length && IsVariableChar(line[pos]))
                    {
                        pos++;
                    }
                }

                if (pos > start + 1)
                {
                    spans.Add(new StyledSpan(start, pos - start, TokenKind.Constant));
                }

                continue;
            }

            // Comments after instruction (e.g., in RUN lines)
            if (ch == '#')
            {
                spans.Add(new StyledSpan(pos, line.Length - pos, TokenKind.Comment));
                break;
            }

            // Flags like --from=builder (must be checked before word boundary)
            if (ch == '-' && pos + 1 < line.Length && line[pos + 1] == '-')
            {
                int start = pos;
                pos += 2;

                while (pos < line.Length && (IsWordChar(line[pos]) || line[pos] == '-'))
                {
                    pos++;
                }

                spans.Add(new StyledSpan(start, pos - start, TokenKind.Attribute));
                continue;
            }

            // Word boundary — check for AS keyword
            if (IsWordChar(ch))
            {
                int start = pos;

                while (pos < line.Length && IsWordChar(line[pos]))
                {
                    pos++;
                }

                string word = line[start..pos];

                if (word.Equals("AS", StringComparison.OrdinalIgnoreCase))
                {
                    spans.Add(new StyledSpan(start, pos - start, TokenKind.Keyword));
                }

                continue;
            }

            pos++;
        }

        return spans.Count == 0
            ? new StyledLine(line, null)
            : new StyledLine(line, [.. spans]);
    }

    private static bool IsInstructionChar(char ch) =>
        ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_';

    private static bool IsVariableChar(char ch) =>
        ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_';

    private static bool IsWordChar(char ch) =>
        ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-';
}
