using Wade.Terminal;

namespace Wade.Highlighting;

internal static class SyntaxTheme
{
    // VS Code Dark+ inspired palette
    private static readonly CellStyle PlainStyle = new(new Color(200, 200, 200), null);
    private static readonly CellStyle KeywordStyle = new(new Color(86, 156, 214), null, Bold: true); // blue
    private static readonly CellStyle StringStyle = new(new Color(206, 145, 120), null); // salmon
    private static readonly CellStyle CommentStyle = new(new Color(106, 153, 85), null); // green
    private static readonly CellStyle NumberStyle = new(new Color(181, 206, 168), null); // light green
    private static readonly CellStyle TypeStyle = new(new Color(78, 201, 176), null); // teal
    private static readonly CellStyle AttributeStyle = new(new Color(156, 220, 254), null); // light blue
    private static readonly CellStyle OperatorStyle = new(new Color(180, 180, 180), null); // gray
    private static readonly CellStyle PunctStyle = new(new Color(180, 180, 180), null); // gray
    private static readonly CellStyle ConstantStyle = new(new Color(86, 156, 214), null); // blue (same as keyword)
    private static readonly CellStyle BuiltinStyle = new(new Color(220, 220, 170), null); // yellow
    private static readonly CellStyle HeadingStyle = new(new Color(86, 156, 214), null, Bold: true); // blue bold
    private static readonly CellStyle BoldStyle = new(new Color(200, 200, 200), null, Bold: true);
    private static readonly CellStyle ItalicStyle = new(new Color(200, 200, 200), null, Dim: true);
    private static readonly CellStyle LinkStyle = new(new Color(78, 201, 176), null); // teal
    private static readonly CellStyle CodeSpanStyle = new(new Color(206, 145, 120), null); // salmon
    private static readonly CellStyle TagNameStyle = new(new Color(86, 156, 214), null); // blue
    private static readonly CellStyle AttrNameStyle = new(new Color(156, 220, 254), null); // light blue
    private static readonly CellStyle AttrValueStyle = new(new Color(206, 145, 120), null); // salmon
    private static readonly CellStyle KeyStyle = new(new Color(156, 220, 254), null); // light blue
    private static readonly CellStyle DirectiveStyle = new(new Color(155, 155, 155), null); // dim gray

    public static CellStyle GetStyle(TokenKind kind) => kind switch
    {
        TokenKind.Keyword => KeywordStyle,
        TokenKind.String => StringStyle,
        TokenKind.Comment => CommentStyle,
        TokenKind.Number => NumberStyle,
        TokenKind.Type => TypeStyle,
        TokenKind.Attribute => AttributeStyle,
        TokenKind.Operator => OperatorStyle,
        TokenKind.Punctuation => PunctStyle,
        TokenKind.Constant => ConstantStyle,
        TokenKind.BuiltinFunc => BuiltinStyle,
        TokenKind.Heading => HeadingStyle,
        TokenKind.Bold => BoldStyle,
        TokenKind.Italic => ItalicStyle,
        TokenKind.Link => LinkStyle,
        TokenKind.CodeSpan => CodeSpanStyle,
        TokenKind.TagName => TagNameStyle,
        TokenKind.AttrName => AttrNameStyle,
        TokenKind.AttrValue => AttrValueStyle,
        TokenKind.Key => KeyStyle,
        TokenKind.Directive => DirectiveStyle,
        _ => PlainStyle,
    };
}
