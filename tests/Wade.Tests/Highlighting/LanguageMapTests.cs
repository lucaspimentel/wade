using Wade.Highlighting;
using Wade.Highlighting.Languages;

namespace Wade.Tests.Highlighting;

public class LanguageMapTests
{
    [Theory]
    [InlineData("Program.cs", typeof(CSharpLanguage))]
    [InlineData("app.py", typeof(PythonLanguage))]
    [InlineData("main.go", typeof(GoLanguage))]
    [InlineData("lib.rs", typeof(RustLanguage))]
    [InlineData("index.js", typeof(JavaScriptLanguage))]
    [InlineData("app.ts", typeof(TypeScriptLanguage))]
    [InlineData("Main.java", typeof(JavaLanguage))]
    [InlineData("script.sh", typeof(ShellLanguage))]
    [InlineData("Deploy.ps1", typeof(PowerShellLanguage))]
    [InlineData("styles.css", typeof(CssLanguage))]
    [InlineData("data.json", typeof(JsonLanguage))]
    [InlineData("Cargo.toml", typeof(TomlLanguage))]
    [InlineData("config.yaml", typeof(YamlLanguage))]
    [InlineData("config.yml", typeof(YamlLanguage))]
    [InlineData("index.html", typeof(XmlHtmlLanguage))]
    [InlineData("pom.xml", typeof(XmlHtmlLanguage))]
    [InlineData("Wade.csproj", typeof(XmlHtmlLanguage))]
    [InlineData("Wade.slnx", typeof(XmlHtmlLanguage))]
    [InlineData("Directory.Build.props", typeof(XmlHtmlLanguage))]
    [InlineData("README.md", typeof(MarkdownLanguage))]
    [InlineData("notes.markdown", typeof(MarkdownLanguage))]
    public void KnownExtension_ReturnsCorrectLanguage(string fileName, Type expectedType)
    {
        ILanguage? lang = LanguageMap.GetLanguage(fileName);
        Assert.NotNull(lang);
        Assert.IsType(expectedType, lang);
    }

    [Theory]
    [InlineData("file.unknown")]
    [InlineData("file.xyz")]
    [InlineData("file.bin")]
    public void UnknownExtension_ReturnsNull(string fileName)
    {
        ILanguage? lang = LanguageMap.GetLanguage(fileName);
        Assert.Null(lang);
    }

    [Theory]
    [InlineData("Program.CS")]
    [InlineData("App.PY")]
    [InlineData("index.JS")]
    public void CaseInsensitive_ExtensionMatches(string fileName)
    {
        ILanguage? lang = LanguageMap.GetLanguage(fileName);
        Assert.NotNull(lang);
    }

    [Theory]
    [InlineData("Dockerfile")]
    [InlineData("Containerfile")]
    [InlineData("build.dockerfile")]
    public void Dockerfile_ReturnsDockerfileLanguage(string fileName)
    {
        ILanguage? lang = LanguageMap.GetLanguage(fileName);
        Assert.IsType<DockerfileLanguage>(lang);
    }
}
