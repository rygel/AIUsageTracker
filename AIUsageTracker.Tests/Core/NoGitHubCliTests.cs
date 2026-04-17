// <copyright file="NoGitHubCliTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.RegularExpressions;

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
        ".git",
    };

    [Fact]
    public void NoGitHubCliSpawnsInSourceCode()
    {
        var repoRoot = GetRepoRoot();
        var csFiles = Directory.GetFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !ExcludedPaths.Any(e => f.Contains(e, StringComparison.Ordinal)))
            .ToList();

        var violations = new List<string>();

        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            var lines = content.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                var regexTimeout = TimeSpan.FromSeconds(1);
                if (Regex.IsMatch(line, @"FileName\s*=\s*[string.Empty']gh[string.Empty']", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, regexTimeout) ||
                    Regex.IsMatch(line, @"Arguments\s*=\s*[string.Empty'].*gh.*[string.Empty']", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, regexTimeout) ||
                    Regex.IsMatch(line, @"gh\s+auth\s+token", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, regexTimeout) ||
                    Regex.IsMatch(line, @"Process\.Start.*gh", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, regexTimeout))
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
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find repo root");
    }
}
