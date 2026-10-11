using System.Text.Json;
using System.Text.RegularExpressions;

namespace PersonalPlatform.ArchitectureTests;

/// <summary>Repository-wide source rules that the compiler cannot enforce.</summary>
public sealed partial class SourceRulesTests
{
    private static readonly string Root = FindRoot();
    private static readonly string Src = Path.Combine(Root, "src");
    private static readonly string ClientDir = Path.Combine(Src, "PersonalPlatform.Client");

    [Fact]
    public void Client_never_renders_raw_html()
    {
        Assert.Empty(Matches(ClientDir, ["*.razor", "*.cs"], "MarkupString"));
        Assert.Empty(Matches(Path.Combine(ClientDir, "wwwroot", "js"), ["*.js"], @"innerHTML|outerHTML|insertAdjacentHTML|document\.write|\beval\s*\(|new\s+Function\s*\("));
    }

    [Fact]
    public void Client_has_no_inline_styles_or_handlers_that_the_csp_would_block()
    {
        Assert.Empty(Matches(ClientDir, ["*.razor"], @"\sstyle\s*="));
        Assert.Empty(Matches(Path.Combine(ClientDir, "wwwroot"), ["*.html"], @"\son[a-z]+\s*=|\sstyle\s*="));
    }

    [Fact]
    public void Client_never_persists_tokens_in_web_storage()
    {
        // The documentation comment in AuthSession mentions the APIs by name; code must never call them.
        Assert.Empty(Matches(ClientDir, ["*.razor", "*.cs", "*.js"], @"(localStorage|sessionStorage|indexedDB)\s*[.\[]"));
    }

    [Fact]
    public void Sql_is_never_built_from_raw_strings() =>
        Assert.Empty(Matches(Src, ["*.cs"], @"FromSqlRaw|ExecuteSqlRaw|SqlQueryRaw|NpgsqlCommand\s*\("));

    [Fact]
    public void Query_filters_are_bypassed_only_in_reviewed_places()
    {
        var files = Matches(Src, ["*.cs"], @"IgnoreQueryFilters\s*\(").Select(m => m.File).Distinct().ToList();

        // AuthService reads the display name for account emails before any user is authenticated, always by user id.
        Assert.Equal(["PersonalPlatform.Infrastructure/Identity/AuthService.cs"], files);
    }

    [Fact]
    public void Source_contains_no_placeholders_or_stubs() =>
        Assert.Empty(Matches(Src, ["*.cs", "*.razor", "*.js"], @"\bTODO\b|\bFIXME\b|NotImplementedException"));

    [Fact]
    public void Configuration_files_contain_no_secrets()
    {
        var findings = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Src, "appsettings*.json", SearchOption.AllDirectories).Where(NotBuildOutput))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            Walk(json.RootElement, Path.GetFileName(file), findings);
        }

        Assert.Empty(findings);

        static void Walk(JsonElement element, string path, List<string> findings)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String && IsSecretName(property.Name) && property.Value.GetString() is { Length: > 0 })
                        {
                            findings.Add($"{path}:{property.Name}");
                        }

                        Walk(property.Value, $"{path}:{property.Name}", findings);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item, path, findings);
                    }

                    break;
                case JsonValueKind.String when PasswordInConnectionString().IsMatch(element.GetString()!):
                    findings.Add($"{path} (connection string password)");
                    break;
            }
        }

        static bool IsSecretName(string name) =>
            name.Contains("password", StringComparison.OrdinalIgnoreCase)
            || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Key", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("ApiKey", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repository_contains_no_private_keys_or_env_files()
    {
        var tracked = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Where(NotBuildOutput)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.DoesNotContain(tracked, f => Path.GetFileName(f) == ".env" || Path.GetExtension(f) is ".pfx" or ".p12" or ".key" or ".pem");
        var withKeys = tracked
            .Where(f => Path.GetExtension(f) is ".cs" or ".json" or ".yml" or ".yaml" or ".sh" or ".sql" or ".md" or ".conf")
            .Where(f => File.ReadAllText(f) is var text
                && (text.Contains("-----BEGIN " + "PRIVATE KEY-----", StringComparison.Ordinal) || text.Contains("-----BEGIN RSA " + "PRIVATE KEY-----", StringComparison.Ordinal)))
            .ToList();
        Assert.Empty(withKeys);
    }

    [Fact]
    public void Logging_calls_never_include_credentials()
    {
        // Structured log templates must not carry passwords, tokens or cookies as named placeholders.
        Assert.Empty(Matches(Src, ["*.cs"], @"Message\s*=\s*""[^""]*\{(?i:Password|Token|RefreshToken|AccessToken|Cookie|Secret|Hash)(:[^}]*)?\}"));
        Assert.Empty(Matches(Src, ["*.cs"], @"\.Log(Information|Warning|Error|Debug|Trace|Critical)\([^;]*\{(?i:Password|Token|RefreshToken|AccessToken|Cookie|Secret|Hash)(:[^}]*)?\}"));
    }

    private static List<(string File, int Line, string Text)> Matches(string directory, string[] patterns, string regex)
    {
        var expression = new Regex(regex, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return
        [
            .. patterns
                .SelectMany(p => Directory.EnumerateFiles(directory, p, SearchOption.AllDirectories))
                .Where(NotBuildOutput)
                .SelectMany(file => File.ReadLines(file).Select((text, index) => (File: Path.GetRelativePath(Src, file).Replace('\\', '/'), Line: index + 1, Text: text.Trim())))
                .Where(line => expression.IsMatch(line.Text)),
        ];
    }

    private static bool NotBuildOutput(string path)
    {
        var separator = Path.DirectorySeparatorChar;
        return !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
            && !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
            && !path.Contains($"{separator}node_modules{separator}", StringComparison.Ordinal)
            && !path.Contains($"{separator}TestResults{separator}", StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PersonalPlatform.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (PersonalPlatform.slnx) not found.");
    }

    [GeneratedRegex(@"Password\s*=\s*[^;\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordInConnectionString();
}
