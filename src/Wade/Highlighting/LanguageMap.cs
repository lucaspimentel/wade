using System.Collections.Frozen;
using Wade.Highlighting.Languages;

namespace Wade.Highlighting;

internal static class LanguageMap
{
    private static readonly CSharpLanguage CSharp = new();
    private static readonly JavaScriptLanguage JavaScript = new();
    private static readonly TypeScriptLanguage TypeScript = new();
    private static readonly PythonLanguage Python = new();
    private static readonly GoLanguage Go = new();
    private static readonly RustLanguage Rust = new();
    private static readonly JavaLanguage Java = new();
    private static readonly ShellLanguage Shell = new();
    private static readonly PowerShellLanguage PowerShell = new();
    private static readonly CssLanguage Css = new();
    private static readonly JsonLanguage Json = new();
    private static readonly TomlLanguage Toml = new();
    private static readonly YamlLanguage Yaml = new();
    private static readonly XmlHtmlLanguage XmlHtml = new();
    private static readonly MarkdownLanguage Markdown = new();
    private static readonly GitIgnoreLanguage GitIgnore = new();
    private static readonly DockerfileLanguage Dockerfile = new();

    private static readonly FrozenDictionary<string, ILanguage> ByExtension =
        new Dictionary<string, ILanguage>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = CSharp,
            [".csx"] = CSharp,
            [".js"] = JavaScript,
            [".mjs"] = JavaScript,
            [".cjs"] = JavaScript,
            [".jsx"] = JavaScript,
            [".ts"] = TypeScript,
            [".tsx"] = TypeScript,
            [".py"] = Python,
            [".go"] = Go,
            [".rs"] = Rust,
            [".java"] = Java,
            [".sh"] = Shell,
            [".bash"] = Shell,
            [".zsh"] = Shell,
            [".fish"] = Shell,
            [".ps1"] = PowerShell,
            [".psm1"] = PowerShell,
            [".psd1"] = PowerShell,
            [".css"] = Css,
            [".scss"] = Css,
            [".sass"] = Css,
            [".json"] = Json,
            [".slnf"] = Json,
            [".toml"] = Toml,
            [".ini"] = Toml,
            [".cfg"] = Toml,
            [".conf"] = Toml,
            [".properties"] = Toml,
            [".inf"] = Toml,
            [".url"] = Toml,
            [".editorconfig"] = Toml,
            [".gitconfig"] = Toml,
            [".yaml"] = Yaml,
            [".yml"] = Yaml,
            [".xml"] = XmlHtml,
            [".html"] = XmlHtml,
            [".htm"] = XmlHtml,
            [".csproj"] = XmlHtml,
            [".vbproj"] = XmlHtml,
            [".fsproj"] = XmlHtml,
            [".props"] = XmlHtml,
            [".targets"] = XmlHtml,
            [".slnx"] = XmlHtml,
            [".resx"] = XmlHtml,
            [".config"] = XmlHtml,
            [".nuspec"] = XmlHtml,
            [".xaml"] = XmlHtml,
            [".md"] = Markdown,
            [".markdown"] = Markdown,
            [".dockerfile"] = Dockerfile,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> NoExtensionShellNames =
        new[] { "Makefile", "Jenkinsfile", "Brewfile" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, ILanguage> ByFilename =
        new Dictionary<string, ILanguage>(StringComparer.OrdinalIgnoreCase)
        {
            [".gitignore"] = GitIgnore,
            [".dockerignore"] = GitIgnore,
            [".npmignore"] = GitIgnore,
            [".prettierignore"] = GitIgnore,
            [".eslintignore"] = GitIgnore,
            [".gitattributes"] = GitIgnore,
            ["Dockerfile"] = Dockerfile,
            ["Containerfile"] = Dockerfile,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static ILanguage? GetLanguage(string filePath)
    {
        string name = Path.GetFileName(filePath);

        // Check by full filename first (handles dotfiles like .gitignore where
        // Path.GetExtension returns the entire filename)
        if (ByFilename.TryGetValue(name, out ILanguage? filenameLang))
        {
            return filenameLang;
        }

        string ext = Path.GetExtension(filePath);
        if (ext.Length > 0)
        {
            return ByExtension.TryGetValue(ext, out ILanguage? lang) ? lang : null;
        }

        return NoExtensionShellNames.Contains(name) ? Shell : null;
    }
}
