using System.Text.RegularExpressions;

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
        @"catch\s*(\([^)]*\))?\s*\{\s*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly (Regex Pattern, string Description)[] SyncOverAsyncPatterns =
    {
        (new Regex(@"\.GetAwaiter\(\)\.GetResult\(", RegexOptions.Compiled | RegexOptions.CultureInvariant), "GetAwaiter().GetResult()"),
        (new Regex(@"\.Wait\(", RegexOptions.Compiled | RegexOptions.CultureInvariant), ".Wait(...)"),
        (new Regex(@"\.Result\b(?!\s*\?)", RegexOptions.Compiled | RegexOptions.CultureInvariant), ".Result")
    };

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

    private static IEnumerable<string> EnumerateProductionSourceFiles()
    {
        var repoRoot = GetRepoRoot();

        foreach (var projectDirectory in ProductionProjectDirectories)
        {
            var fullProjectDirectory = Path.Combine(repoRoot, projectDirectory);
            if (!Directory.Exists(fullProjectDirectory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(fullProjectDirectory, "*.cs", SearchOption.AllDirectories))
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

    private static string GetRelativePath(string path)
    {
        return Path.GetRelativePath(GetRepoRoot(), path);
    }
}
