// <copyright file="ProviderUsageDetailTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Models
{
    using AIUsageTracker.Core.Models;

    public class ProviderUsageDetailTests
    {
        [Fact]
        public void IsDisplayableSubProviderDetail_DetailTypeQuotaWindow_ReturnsFalse()
        {
            var detail = new ProviderUsageDetail
            {
                Name = "Custom Primary",
                DetailType = ProviderUsageDetailType.QuotaWindow
            };

            Assert.False(detail.IsDisplayableSubProviderDetail());
        }

        [Fact]
        public void IsDisplayableSubProviderDetail_DetailTypeModel_ReturnsTrue()
        {
            var detail = new ProviderUsageDetail
            {
                Name = "GPT-5",
                DetailType = ProviderUsageDetailType.Model
            };

            Assert.True(detail.IsDisplayableSubProviderDetail());
        }

        [Fact]
        public void IsPrimaryQuotaDetail_WithQuotaWindowAndPrimaryWindowKind_ReturnsTrue()
        {
            var detail = new ProviderUsageDetail
            {
                Name = "5-hour quota",
                DetailType = ProviderUsageDetailType.QuotaWindow,
                WindowKind = WindowKind.Primary
            };

            Assert.True(detail.IsPrimaryQuotaDetail());
        }

        [Fact]
        public void IsPrimaryQuotaDetail_WithQuotaWindowAndSecondaryWindowKind_ReturnsFalse()
        {
            var detail = new ProviderUsageDetail
            {
                Name = "Weekly quota",
                DetailType = ProviderUsageDetailType.QuotaWindow,
                WindowKind = WindowKind.Secondary
            };

            Assert.False(detail.IsPrimaryQuotaDetail());
        }

        [Fact]
        public void IsSecondaryQuotaDetail_WithQuotaWindowAndSecondaryWindowKind_ReturnsTrue()
        {
            var detail = new ProviderUsageDetail
            {
                Name = "Weekly quota",
                DetailType = ProviderUsageDetailType.QuotaWindow,
                WindowKind = WindowKind.Secondary
            };

            Assert.True(detail.IsSecondaryQuotaDetail());
        }

        [Fact]
        public void IsWindowQuotaDetail_WithQuotaWindowDetailType_ReturnsTrue()
        {
            var detail = new ProviderUsageDetail
            {
                Name = "5-hour quota",
                DetailType = ProviderUsageDetailType.QuotaWindow,
                WindowKind = WindowKind.Primary
            };

            Assert.True(detail.IsWindowQuotaDetail());
        }

        [Fact]
        public void IsCreditDetail_WithCreditDetailType_ReturnsTrue()
        {
            var detail = new ProviderUsageDetail
            {
                Name = "Credits",
                DetailType = ProviderUsageDetailType.Credit,
                WindowKind = WindowKind.None
            };

            Assert.True(detail.IsCreditDetail());
        }

        [Fact]
        public void IsDisplayableSubProviderDetail_WithOtherDetailType_ReturnsTrue()
        {
            var detail = new ProviderUsageDetail
            {
                Name = "Avg Cost/Day",
                DetailType = ProviderUsageDetailType.Other,
                WindowKind = WindowKind.None
            };

            Assert.True(detail.IsDisplayableSubProviderDetail());
        }

        [Fact]
        public void WindowKind_DefaultValue_IsNone()
        {
            var detail = new ProviderUsageDetail();

            Assert.Equal(WindowKind.None, detail.WindowKind);
        }

        [Fact]
        public void DetailType_DefaultValue_IsUnknown()
        {
            var detail = new ProviderUsageDetail();

            Assert.Equal(ProviderUsageDetailType.Unknown, detail.DetailType);
        }
    }
}
