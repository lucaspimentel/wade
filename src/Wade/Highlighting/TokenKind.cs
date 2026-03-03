namespace Wade.Highlighting;

public enum TokenKind : byte
{
    Plain,
    Keyword,
    String,
    Comment,
    Number,
    Type,
    Attribute,
    Operator,
    Punctuation,
    Constant,
    BuiltinFunc,
    Heading,
    Bold,
    Italic,
    Link,
    CodeSpan,
    TagName,
    AttrName,
    AttrValue,
    Key,
    Directive,
}
