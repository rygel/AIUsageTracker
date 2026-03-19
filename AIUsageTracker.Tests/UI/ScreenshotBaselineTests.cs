// <copyright file="ScreenshotBaselineTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using Codeuctivity.ImageSharpCompare;
using SixLabors.ImageSharp;

namespace AIUsageTracker.Tests.UI;

/// <summary>
/// Perceptual screenshot baseline tests. Compares freshly-rendered screenshots against
/// committed baselines using ImageSharpCompare with a pixel-error-percentage tolerance,
/// avoiding false positives from WPF ClearType subpixel rendering variance.
///
/// Requires two environment variables set by the CI workflow before running:
///   SCREENSHOT_BASELINE_DIR  — directory containing the committed baseline PNGs
///   SCREENSHOT_CANDIDATE_DIR — directory containing the freshly-generated PNGs
///
/// When either variable is absent the tests are skipped (they will report "passed"
/// with no assertions, preserving the developer workflow).
/// </summary>
[Trait("Category", "Screenshot")]
public sealed class ScreenshotBaselineTests
{
    /// <summary>
    /// Maximum allowed pixel-error percentage before a screenshot comparison fails.
    /// 1 % provides enough headroom for WPF ClearType subpixel rendering variance
    /// (typically &lt;0.1 %) while still catching meaningful layout regressions,
    /// which typically affect 5 %+ of pixels.
    /// </summary>
    private const double PixelErrorTolerancePercent = 1.0;

    private static readonly string[] ScreenshotFileNames =
    [
        "screenshot_dashboard_privacy.png",
        "screenshot_settings_providers_privacy.png",
        "screenshot_settings_layout_privacy.png",
        "screenshot_settings_history_privacy.png",
        "screenshot_info_privacy.png",
    ];

    public static IEnumerable<object[]> ScreenshotNames =>
        ScreenshotFileNames.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(ScreenshotNames))]
    public void Screenshot_MatchesBaseline_WithinTolerance(string fileName)
    {
        var baselineDir = Environment.GetEnvironmentVariable("SCREENSHOT_BASELINE_DIR");
        var candidateDir = Environment.GetEnvironmentVariable("SCREENSHOT_CANDIDATE_DIR");

        if (string.IsNullOrWhiteSpace(baselineDir) || string.IsNullOrWhiteSpace(candidateDir))
        {
            // Not a CI screenshot run — skip gracefully.
            return;
        }

        var baselinePath = Path.Combine(baselineDir, fileName);
        var candidatePath = Path.Combine(candidateDir, fileName);

        Assert.True(
            File.Exists(baselinePath),
            $"Baseline image not found: {baselinePath}");

        Assert.True(
            File.Exists(candidatePath),
            $"Candidate image not found: {candidatePath} — was screenshot generation run first?");

        var diff = ImageSharpCompare.CalcDiff(candidatePath, baselinePath, ResizeOption.DontResize);

        if (diff.PixelErrorPercentage >= PixelErrorTolerancePercent)
        {
            SaveDiffImage(candidatePath, baselinePath, candidateDir, fileName);
        }

        Assert.True(
            diff.PixelErrorPercentage < PixelErrorTolerancePercent,
            $"{fileName}: pixel error {diff.PixelErrorPercentage:F3}% exceeds tolerance {PixelErrorTolerancePercent}% " +
            $"({diff.PixelErrorCount} pixels differ). " +
            $"Diff image saved to: {Path.Combine(candidateDir, $"diff_{fileName}")}. " +
            $"To accept new baselines, run: git add docs/screenshot_*_privacy.png && git commit -m 'chore: update screenshot baselines'");
    }

    private static void SaveDiffImage(string candidatePath, string baselinePath, string outputDir, string fileName)
    {
        try
        {
            using var diffImage = ImageSharpCompare.CalcDiffMaskImage(candidatePath, baselinePath, ResizeOption.DontResize);
            diffImage.SaveAsPng(Path.Combine(outputDir, $"diff_{fileName}"));
        }
        catch (Exception ex)
        {
            // Diff image generation is best-effort — don't let it mask the real failure.
            Console.WriteLine($"Warning: could not save diff image for {fileName}: {ex.Message}");
        }
    }
}
