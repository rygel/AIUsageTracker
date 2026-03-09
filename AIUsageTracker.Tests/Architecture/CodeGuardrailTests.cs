using System.Text.RegularExpressions;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Architecture;

public class CodeGuardrailTests
{
    private static readonly string[] ProductionProjectDirectories =
    {
        "AIUsageTracker.Core",
        "AIUsageTracker.Infrastructure",
        "AIUsageTracker.Monitor",
        "AIUsageTracker.UI.Slim",
        "AIUsageTracker.Web"
    };

    private static readonly Regex EmptyCatchRegex = new(
        @"catch\s*(?<catchBlock>\([^)]*\))?\s*\{\s*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline,
        TimeSpan.FromMilliseconds(500));

    private static readonly (Regex Pattern, string Description)[] SyncOverAsyncPatterns =
    {
        (
            new Regex(
                @"\.GetAwaiter\(\)\.GetResult\(",
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                TimeSpan.FromMilliseconds(500)),
            "GetAwaiter().GetResult()"),
        (
            new Regex(
                @"\.Wait\(",
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                TimeSpan.FromMilliseconds(500)),
            ".Wait(...)"),
        (
            new Regex(
                @"\.Result\b(?!\s*\?)",
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                TimeSpan.FromMilliseconds(500)),
            ".Result")
    };

    private static readonly Regex StringLiteralRegex = new(
        "\"(?<literal>(?:\\\\.|[^\"\\\\])*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(500));
#pragma warning restore MA0023

    private static readonly HashSet<string> AllowedHardcodedProviderIdFiles = new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void ProductionCode_DoesNotUseSyncOverAsync()
    {
        var violations = new List<string>();

        foreach (var file in EnumerateProductionSourceFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.Contains("architecture-allow-sync-wait", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var (pattern, description) in SyncOverAsyncPatterns)
                {
                    if (pattern.IsMatch(line))
                    {
                        violations.Add($"{GetRelativePath(file)}:{index + 1} uses {description}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Sync-over-async is forbidden in production code." + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ProductionCode_DoesNotContainEmptyCatchBlocks()
    {
        var violations = new List<string>();

        foreach (var file in EnumerateProductionSourceFiles())
        {
            var content = File.ReadAllText(file);
            foreach (Match match in EmptyCatchRegex.Matches(content))
            {
                var line = content[..match.Index].Count(c => c == '\n') + 1;
                violations.Add($"{GetRelativePath(file)}:{line} contains an empty catch block");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Empty catch blocks are forbidden in production code." + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ProductionCode_DoesNotIntroduceHardcodedProviderIdsOutsideAllowedFiles()
    {
        var violations = new List<string>();
        var providerIds = GetKnownProviderIds();

        foreach (var file in EnumerateProductionSourceFiles(includeMarkup: true))
        {
            var relativePath = NormalizePath(GetRelativePath(file));
            if (IsAllowedHardcodedProviderIdFile(relativePath))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.Contains("provider-id-guardrail-allow", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match match in StringLiteralRegex.Matches(line))
                {
                    var literalValue = match.Groups[1].Value;
                    if (!providerIds.Contains(literalValue))
                    {
                        continue;
                    }

                    violations.Add($"{relativePath}:{index + 1} hardcodes provider id \"{literalValue}\"");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Hardcoded provider ids are forbidden outside provider metadata/compatibility files." +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }
`n
    private static IEnumerable<string> EnumerateProductionSourceFiles(bool includeMarkup = false)
    {
        var repoRoot = GetRepoRoot();
        var extensions = includeMarkup
            ? new[] { "*.cs", "*.cshtml" }
            : new[] { "*.cs" };

        foreach (var projectDirectory in ProductionProjectDirectories)
        {
            var fullProjectDirectory = Path.Combine(repoRoot, projectDirectory);
            if (!Directory.Exists(fullProjectDirectory))
            {
                continue;
            }

            foreach (var extension in extensions)
            {
                foreach (var file in Directory.EnumerateFiles(fullProjectDirectory, extension, SearchOption.AllDirectories))
                {
                    if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    yield return file;
                }
            }
        }
    }
`n
    private static string GetRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AIUsageTracker.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
`n
    private static string GetRelativePath(string path)
    {
        return Path.GetRelativePath(GetRepoRoot(), path);
    }
`n
    private static HashSet<string> GetKnownProviderIds()
    {
        return ProviderMetadataCatalog.Definitions
            .SelectMany(definition => definition.HandledProviderIds
                .Concat(definition.NonPersistedProviderIds)
                .Concat(definition.VisibleDerivedProviderIds))
            .ToHashSet(StringComparer.Ordinal);
    }
`n
    private static bool IsAllowedHardcodedProviderIdFile(string relativePath)
    {
        return relativePath.StartsWith(NormalizePath("AIUsageTracker.Infrastructure/Providers/"), StringComparison.OrdinalIgnoreCase) ||
               AllowedHardcodedProviderIdFiles.Contains(relativePath);
    }
`n
    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
