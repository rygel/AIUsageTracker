// <copyright file="ProviderSubDetailPresentationCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.UI
{
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.UI.Slim;

    public sealed class ProviderSubDetailPresentationCatalogTests
    {
        [Fact]
        public void GetDisplayableDetails_FiltersAndSorts()
        {
            var usage = new ProviderUsage
            {
                Details = new List<ProviderUsageDetail>
                {
                    new() { Name = "Beta", DetailType = ProviderUsageDetailType.Other },
                    new() { Name = "Alpha", DetailType = ProviderUsageDetailType.Model },
                    new() { Name = "Ignored", DetailType = ProviderUsageDetailType.Credit },
                    new() { Name = string.Empty, DetailType = ProviderUsageDetailType.Model }
                }
            };

            var details = ProviderSubDetailPresentationCatalog.GetDisplayableDetails(usage);

            Assert.Equal(new[] { "Alpha", "Beta" }, details.Select(detail => detail.Name).ToArray());
        }

        [Fact]
        public void Create_UsesPercentDisplay_WhenPercentAvailable()
        {
            var detail = new ProviderUsageDetail
            {
                Used = "35% used",
                NextResetTime = new DateTime(2026, 3, 7, 9, 0, 0)
            };

            var presentation = ProviderSubDetailPresentationCatalog.Create(
                detail,
                isQuotaBased: false,
                showUsed: true,
                _ => "2h 0m");

            Assert.True(presentation.HasProgress);
            Assert.Equal(35, presentation.UsedPercent);
            Assert.Equal(35, presentation.IndicatorWidth);
            Assert.Equal("35%", presentation.DisplayText);
            Assert.Equal("(2h 0m)", presentation.ResetText);
        }

        [Fact]
        public void Create_FallsBackToRawValue_WhenPercentUnavailable()
        {
            var detail = new ProviderUsageDetail
            {
                Used = "Unlimited"
            };

            var presentation = ProviderSubDetailPresentationCatalog.Create(
                detail,
                isQuotaBased: false,
                showUsed: false,
                _ => "ignored");

            Assert.False(presentation.HasProgress);
            Assert.Equal("Unlimited", presentation.DisplayText);
            Assert.Null(presentation.ResetText);
        }
    }
}
