// <copyright file="SlimGroupedUsageGuardrailTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Architecture;

public class SlimGroupedUsageGuardrailTests
{
    [Fact]
    public void MainWindow_UsesGroupedUsageEndpointForDisplayData()
    {
        var source = File.ReadAllText(GetRepoPath("AIUsageTracker.UI.Slim", "MainWindow.xaml.cs"));

        Assert.Contains("GetGroupedUsageAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_monitorService.GetUsageAsync()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_monitorService.GetProviderCapabilitiesAsync()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsWindow_UsesGroupedUsageEndpointForDisplayData()
    {
        var source = File.ReadAllText(GetRepoPath("AIUsageTracker.UI.Slim", "SettingsWindow.xaml.cs"));

        Assert.Contains("GetGroupedUsageAsync", source, StringComparison.Ordinal);
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
                Missing = ProviderMetadataCatalog.GetMissingDerivedModelSelectorProviderIds(definition.ProviderId),
            })
            .Where(entry => entry.Missing.Count > 0)
            .ToList();

        Assert.True(
            missingSelectors.Count == 0,
            $"Missing derived model selectors: {string.Join("; ", missingSelectors.Select(entry => $"{entry.ProviderId}: {string.Join(", ", entry.Missing)}"))}");
    }

    [Fact]
    public void DerivedModelDisplayNaming_UsesProviderCatalog_NotGeminiSpecificHardcodes()
    {
        var adapterSource = File.ReadAllText(GetRepoPath("AIUsageTracker.UI.Slim", "GroupedUsageDisplayAdapter.cs"));
        Assert.Contains("GetDerivedModelDisplayName", adapterSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[Gemini CLI]", adapterSource, StringComparison.Ordinal);

        var geminiSource = File.ReadAllText(GetRepoPath("AIUsageTracker.Infrastructure", "Providers", "GeminiProvider.cs"));
        Assert.Contains("visibleDerivedProviderIds", geminiSource, StringComparison.Ordinal);
        Assert.Contains("derivedModelDisplaySuffix: \"[Gemini CLI]\"", geminiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderCapabilityCatalog_IsMetadataOnly_WithoutSnapshotOverrides()
    {
        var source = File.ReadAllText(GetRepoPath("AIUsageTracker.UI.Slim", "ProviderCapabilityCatalog.cs"));

        Assert.DoesNotContain("AgentProviderCapabilitiesSnapshot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("snapshot", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProviderMetadataCatalog", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SlimQuotaPresentation_UsesQuotaBucketKindNaming()
    {
        var files = new[]
        {
            GetRepoPath("AIUsageTracker.UI.Slim", "ProviderDualQuotaBucketPresentationCatalog.cs"),
            GetRepoPath("AIUsageTracker.UI.Slim", "ProviderTooltipPresentationCatalog.cs"),
            GetRepoPath("AIUsageTracker.UI.Slim", "ProviderSubDetailPresentationCatalog.cs"),
        };

        foreach (var path in files)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain(".WindowKind", source, StringComparison.Ordinal);
            Assert.Contains(".QuotaBucketKind", source, StringComparison.Ordinal);
        }

        var groupedAdapterSource = File.ReadAllText(GetRepoPath("AIUsageTracker.UI.Slim", "GroupedUsageDisplayAdapter.cs"));
        Assert.DoesNotContain(".WindowKind", groupedAdapterSource, StringComparison.Ordinal);
        Assert.Contains("QuotaBucketKind =", groupedAdapterSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderUsageDetail_UsesQuotaBucketKindAsCanonicalContractProperty()
    {
        var source = File.ReadAllText(GetRepoPath("AIUsageTracker.Core", "Models", "ProviderUsageDetail.cs"));

        Assert.Contains("[JsonPropertyName(\"window_kind\")]", source, StringComparison.Ordinal);
        Assert.Contains("public WindowKind QuotaBucketKind", source, StringComparison.Ordinal);
        Assert.Contains("[JsonIgnore]", source, StringComparison.Ordinal);
        Assert.Contains("[Obsolete(\"Use QuotaBucketKind.\")]", source, StringComparison.Ordinal);
        Assert.Contains("public WindowKind WindowKind", source, StringComparison.Ordinal);
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
}
