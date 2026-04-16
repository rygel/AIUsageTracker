// <copyright file="SlimGroupedUsageGuardrailTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Architecture;

public class SlimGroupedUsageGuardrailTests
{
    [Fact]
    public void MainWindow_DoesNotUseDeprecatedUsageEndpoints()
    {
        var source = File.ReadAllText(GetRepoPath("AIUsageTracker.UI.Slim", "MainWindow.xaml.cs"));

        Assert.DoesNotContain("_monitorService.GetUsageAsync()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_monitorService.GetProviderCapabilitiesAsync()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsWindow_DoesNotUseDeprecatedUsageEndpoints()
    {
        var source = File.ReadAllText(GetRepoPath("AIUsageTracker.UI.Slim", "SettingsWindow.xaml.cs"));

        Assert.DoesNotContain("_monitorService.GetUsageAsync()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_monitorService.GetProviderCapabilitiesAsync()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderDefinitions_WithVisibleDerivedRows_DefineSelectorsForThoseRows()
    {
        var missingSelectors = ProviderMetadataCatalog.Definitions
            .Select(definition => new
            {
                definition.ProviderId,
                Missing = GetMissingDerivedModelSelectorProviderIds(definition.ProviderId),
            })
            .Where(entry => entry.Missing.Count > 0)
            .ToList();

        Assert.True(
            missingSelectors.Count == 0,
            $"Missing derived model selectors: {string.Join("; ", missingSelectors.Select(entry => $"{entry.ProviderId}: {string.Join(", ", entry.Missing)}"))}");
    }

    [Fact]
    public void DerivedModelDisplayNaming_DoesNotHardcodeGeminiSuffix_InDisplayAdapter()
    {
        var adapterSource = File.ReadAllText(GetRepoPath("AIUsageTracker.UI.Slim", "GroupedUsageDisplayAdapter.cs"));
        Assert.DoesNotContain("[Gemini CLI]", adapterSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SlimQuotaPresentation_DoesNotUseRemovedDetailsProperty()
    {
        // ProviderUsageDetail and ProviderUsage.Details were removed in the flat-card refactor.
        // Presentation code must not reference the old Details-based patterns.
        var files = new[]
        {
            GetRepoPath("AIUsageTracker.UI.Slim", "MainWindowRuntimeLogic.cs"),
            GetRepoPath("AIUsageTracker.UI.Slim", "GroupedUsageDisplayAdapter.cs"),
            GetRepoPath("AIUsageTracker.UI.Slim", "ProviderCardRenderer.cs"),
        };

        foreach (var path in files)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain(".Details", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ProviderUsageDetail", source, StringComparison.Ordinal);
        }
    }

    private static string GetRepoPath(params string[] segments)
    {
        var root = GetRepoRoot();
        return Path.Combine([root, .. segments]);
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

    private static IReadOnlyList<string> GetMissingDerivedModelSelectorProviderIds(string providerId)
    {
        var def = ProviderMetadataCatalog.Find(providerId);
        var visibleDerivedProviderIds = def?.VisibleDerivedProviderIds ?? (IReadOnlyCollection<string>)Array.Empty<string>();
        var selectorProviderIds = (def?.DerivedModelSelectors ?? (IReadOnlyCollection<ProviderDerivedModelSelector>)Array.Empty<ProviderDerivedModelSelector>())
            .Select(selector => selector.DerivedProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return visibleDerivedProviderIds
            .Where(derivedProviderId => !selectorProviderIds.Contains(derivedProviderId))
            .OrderBy(derivedProviderId => derivedProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
