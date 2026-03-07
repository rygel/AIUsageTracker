using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderCardPresentationCatalogTests
{
    [Fact]
    public void Create_ReturnsMissingStatus_ForMissingKeyDescription()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "openai",
            Description = "API key not found",
            IsAvailable = false
        };

        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed: false);

        Assert.True(presentation.IsMissing);
        Assert.Equal("Key Missing", presentation.StatusText);
        Assert.Equal(ProviderCardStatusTone.Missing, presentation.StatusTone);
        Assert.False(presentation.ShouldHaveProgress);
    }

    [Fact]
    public void Create_ReturnsAntigravityParentStatus_WhenDescriptionMissing()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "antigravity",
            IsAvailable = true,
            IsQuotaBased = true
        };

        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed: false);

        Assert.True(presentation.IsAntigravityParent);
        Assert.Equal("Per-model quotas", presentation.StatusText);
        Assert.False(presentation.ShouldHaveProgress);
    }

    [Fact]
    public void Create_FormatsQuotaFractionStatus_WhenDisplayAsFraction()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "synthetic",
            IsAvailable = true,
            IsQuotaBased = true,
            DisplayAsFraction = true,
            RequestsUsed = 40,
            RequestsAvailable = 100,
            RequestsPercentage = 60
        };

        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed: false);

        Assert.Equal("60 / 100 remaining", presentation.StatusText);
        Assert.True(presentation.ShouldHaveProgress);
        Assert.Equal(40, presentation.UsedPercent);
        Assert.Equal(60, presentation.RemainingPercent);
    }

    [Fact]
    public void Create_KeepsDescription_ForStatusUsage()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "mistral",
            Description = "Connected",
            IsAvailable = true,
            IsQuotaBased = true,
            UsageUnit = "Status",
            RequestsPercentage = 70
        };

        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed: true);

        Assert.Equal("Connected", presentation.StatusText);
    }

    [Fact]
    public void Create_FormatsUsagePlanPercentStatus()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "opencode",
            IsAvailable = true,
            PlanType = PlanType.Usage,
            RequestsAvailable = 100,
            RequestsPercentage = 25
        };

        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed: false);

        Assert.Equal("75% remaining", presentation.StatusText);
    }

    [Fact]
    public void Create_FormatsDualWindowStatus_AndSuppressesSingleResetTime()
    {
        var usage = new ProviderUsage
        {
            ProviderId = "codex",
            IsAvailable = true,
            IsQuotaBased = true,
            RequestsPercentage = 96,
            NextResetTime = new DateTime(2026, 3, 7, 1, 0, 0),
            Details = new List<ProviderUsageDetail>
            {
                new()
                {
                    Name = "5-hour quota",
                    Used = "96% remaining (4% used)",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    WindowKind = WindowKind.Primary
                },
                new()
                {
                    Name = "Weekly quota",
                    Used = "49% remaining (51% used)",
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    WindowKind = WindowKind.Secondary
                }
            }
        };

        var presentation = ProviderCardPresentationCatalog.Create(usage, showUsed: false);

        Assert.Equal("5-hour 96% remaining | Weekly 49% remaining", presentation.StatusText);
        Assert.True(presentation.SuppressSingleResetTime);
    }
}
