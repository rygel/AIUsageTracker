using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace AIUsageTracker.Tests.Core;

public class NoGitHubCliTests
{
    private static readonly string[] ExcludedPaths = new[]
    {
        "release-",
        "ci-hang-",
        "synthetic-hotfix-",
        "appcast-sync-",
        "node_modules",
        ".git"
    };

    [Fact]
    public void NoGitHubCliSpawnsInSourceCode()
    {
        var repoRoot = GetRepoRoot();
        var csFiles = Directory.GetFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !ExcludedPaths.Any(e => f.Contains(e)))
            .ToList();

        var violations = new List<string>();

        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            var lines = content.Split('\n');
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                if (Regex.IsMatch(line, @"FileName\s*=\s*[""']gh[""']", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(line, @"Arguments\s*=\s*[""'].*gh.*[""']", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(line, @"gh\s+auth\s+token", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(line, @"Process\.Start.*gh", RegexOptions.IgnoreCase))
                {
                    violations.Add($"{file}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.Empty(violations);
    }

    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find repo root");
    }
}
