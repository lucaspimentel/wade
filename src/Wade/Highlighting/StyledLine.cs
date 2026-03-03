namespace Wade.Highlighting;

internal readonly record struct StyledSpan(int Start, int Length, TokenKind Kind);

internal readonly record struct StyledLine(string Text, StyledSpan[]? Spans);
