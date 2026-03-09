// <copyright file="ProviderSubTrayCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.UI
{
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.UI.Slim;

    public sealed class ProviderSubTrayCatalogTests
    {
        [Fact]
        public void GetEligibleDetails_FiltersDeduplicatesAndSorts()
        {
            var usage = new ProviderUsage
            {
                Details = new List<ProviderUsageDetail>
                {
                    new() { Name = "Gemini 2.5 Pro", Used = "45% used", DetailType = ProviderUsageDetailType.Model },
                    new() { Name = "Gemini 2.5 Flash", Used = "12% used", DetailType = ProviderUsageDetailType.Model },
                    new() { Name = "Gemini 2.5 Pro", Used = "50% used", DetailType = ProviderUsageDetailType.Model },
                    new() { Name = "[internal]", Used = "10% used", DetailType = ProviderUsageDetailType.Model },
                    new() { Name = "Credits", Used = "Unlimited", DetailType = ProviderUsageDetailType.Credit }
                }
            };

            var details = ProviderSubTrayCatalog.GetEligibleDetails(usage);

            Assert.Equal(
                new[] { "Gemini 2.5 Flash", "Gemini 2.5 Pro" },
                details.Select(detail => detail.Name).ToArray());
        }

        [Fact]
        public void GetEligibleDetails_ReturnsEmpty_WhenUsageMissing()
        {
            var details = ProviderSubTrayCatalog.GetEligibleDetails(null);

            Assert.Empty(details);
        }
    }
}
