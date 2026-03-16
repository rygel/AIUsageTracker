// <copyright file="UiThreadAffinityGuardrailTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Architecture;

public class UiThreadAffinityGuardrailTests
{
    [Fact]
    public void UiBoundCode_DoesNotUseConfigureAwaitFalse()
    {
        var repoRoot = GetRepoRoot();
        var uiSlimRoot = Path.Combine(repoRoot, "AIUsageTracker.UI.Slim");
        var violations = new List<string>();

        // Scan all xaml.cs code-behind files and ViewModel source files
        var files = Directory.EnumerateFiles(uiSlimRoot, "*.xaml.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(Path.Combine(uiSlimRoot, "ViewModels"), "*.cs", SearchOption.TopDirectoryOnly));

        foreach (var fullPath in files)
        {
            var relativePath = NormalizePath(Path.GetRelativePath(repoRoot, fullPath));
            var lines = File.ReadAllLines(fullPath);
            for (var index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains("ui-thread-guardrail-allow", StringComparison.Ordinal))
                {
                    continue;
                }

                if (lines[index].Contains("ConfigureAwait(false)", StringComparison.Ordinal))
                {
                    violations.Add($"{relativePath}:{index + 1} uses ConfigureAwait(false).");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "UI-bound code must not use ConfigureAwait(false) because it can resume off-dispatcher and crash with cross-thread access."
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static string GetRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AIUsageTracker.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
