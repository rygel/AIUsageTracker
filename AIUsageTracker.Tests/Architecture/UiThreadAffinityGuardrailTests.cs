// <copyright file="UiThreadAffinityGuardrailTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Architecture;

public class UiThreadAffinityGuardrailTests
{
    private static readonly string[] UiBoundFiles =
    {
        "AIUsageTracker.UI.Slim/MainWindow.xaml.cs",
        "AIUsageTracker.UI.Slim/SettingsWindow.xaml.cs",
        "AIUsageTracker.UI.Slim/InfoDialog.xaml.cs",
        "AIUsageTracker.UI.Slim/ViewModels/MainViewModel.cs",
        "AIUsageTracker.UI.Slim/ViewModels/SettingsViewModel.cs",
    };

    [Fact]
    public void UiBoundCode_DoesNotUseConfigureAwaitFalse()
    {
        var repoRoot = GetRepoRoot();
        var violations = new List<string>();

        foreach (var relativePath in UiBoundFiles)
        {
            var fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                violations.Add($"{relativePath} is missing.");
                continue;
            }

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
            "UI-bound code must not use ConfigureAwait(false) because it can resume off-dispatcher and crash with cross-thread access." +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations));
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
}
