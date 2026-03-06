using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderUsageDisplayCatalogTests
{
    [Fact]
    public void PrepareForMainWindow_FiltersUnavailableParentAndDuplicateProviders()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "openai", IsAvailable = true },
            new() { ProviderId = "openai", IsAvailable = false },
            new() { ProviderId = "antigravity", IsAvailable = false }
        };

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages);

        var displayable = Assert.Single(preparation.DisplayableUsages);
        Assert.Equal("openai", displayable.ProviderId);
        Assert.True(preparation.HasAntigravityParent);
    }

    [Fact]
    public void PrepareForMainWindow_HidesAntigravityChildren_WhenParentExists()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "antigravity", IsAvailable = true },
            new() { ProviderId = "antigravity.gemini-pro", IsAvailable = true }
        };

        var preparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages);

        var displayable = Assert.Single(preparation.DisplayableUsages);
        Assert.Equal("antigravity", displayable.ProviderId);
    }

    [Fact]
    public void CreateAntigravityModelUsages_DeduplicatesAndBuildsSyntheticChildren()
    {
        var parent = new ProviderUsage
        {
            ProviderId = "antigravity",
            IsAvailable = true,
            AuthSource = "test",
            Details = new List<ProviderUsageDetail>
            {
                new() { Name = "Gemini Pro", Used = "75% remaining", NextResetTime = new DateTime(2026, 3, 7, 10, 0, 0) },
                new() { Name = "Gemini Pro", Used = "80% remaining" },
                new() { Name = "[internal]", Used = "10% remaining" },
                new() { Name = "Gemini Flash", Used = "55% remaining" }
            }
        };

        var children = ProviderUsageDisplayCatalog.CreateAntigravityModelUsages(parent);

        Assert.Equal(
            new[] { "antigravity.gemini-flash", "antigravity.gemini-pro" },
            children.Select(child => child.ProviderId).ToArray());
        Assert.All(children, child => Assert.Equal(PlanType.Coding, child.PlanType));
        Assert.All(children, child => Assert.True(child.IsQuotaBased));
    }
}
