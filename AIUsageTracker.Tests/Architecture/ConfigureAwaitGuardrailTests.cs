// <copyright file="ConfigureAwaitGuardrailTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Architecture;

/// <summary>
/// Prevents ConfigureAwait(false) in WPF UI code-behind files.
/// ConfigureAwait(false) in UI code moves continuations off the UI thread,
/// causing silent failures when the continuation tries to update UI elements.
/// This test would have caught the beta.18 regression.
/// </summary>
public class ConfigureAwaitGuardrailTests
{
    [Fact]
    public void WpfCodeBehind_MustNotUse_ConfigureAwaitFalse()
    {
        var uiSlimDir = FindProjectDirectory("AIUsageTracker.UI.Slim");
        if (uiSlimDir == null)
        {
            return;
        }

        // Scan all .xaml.cs files and partial class files for MainWindow/SettingsWindow/InfoDialog
        var uiCodeBehindFiles = Directory.GetFiles(uiSlimDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("obj", StringComparison.Ordinal) && !f.Contains("bin", StringComparison.Ordinal))
            .Where(f =>
                f.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(f).StartsWith("MainWindow.", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(f).StartsWith("SettingsWindow.", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(f).StartsWith("InfoDialog.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var violations = new List<string>();

        foreach (var file in uiCodeBehindFiles)
        {
            var lines = File.ReadAllLines(file);
            var fileName = Path.GetFileName(file);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("ConfigureAwait(false)", StringComparison.Ordinal))
                {
                    // Allow if it's in a comment
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*"))
                    {
                        continue;
                    }

                    // Allow in Task.Run lambdas (background work is OK)
                    // Check if we're inside a Task.Run block by scanning backwards
                    var isInTaskRun = false;
                    for (int j = i; j >= Math.Max(0, i - 10); j--)
                    {
                        if (lines[j].Contains("Task.Run", StringComparison.Ordinal))
                        {
                            isInTaskRun = true;
                            break;
                        }
                    }

                    if (!isInTaskRun)
                    {
                        violations.Add($"{fileName}:{i + 1}: ConfigureAwait(false) in UI code-behind");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"ConfigureAwait(false) in WPF UI code-behind causes UI thread deadlocks.{Environment.NewLine}" +
            $"Use ConfigureAwait(false) only in Core/Infrastructure library code, never in .xaml.cs or Window partial classes.{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    private static string? FindProjectDirectory(string projectName)
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            var candidate = Path.Combine(dir, projectName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
