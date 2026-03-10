// <copyright file="ReleaseUrlCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Core
{
    using AIUsageTracker.Core.Updates;
    using Xunit;

    public class ReleaseUrlCatalogTests
    {
        [Theory]
        [InlineData("x64", false, "https://github.com/rygel/AIUsageTracker/releases/latest/download/appcast_x64.xml")]
        [InlineData("x86", false, "https://github.com/rygel/AIUsageTracker/releases/latest/download/appcast_x86.xml")]
        [InlineData("arm64", false, "https://github.com/rygel/AIUsageTracker/releases/latest/download/appcast_arm64.xml")]
        [InlineData("arm", true, "https://github.com/rygel/AIUsageTracker/releases/latest/download/appcast_beta_arm64.xml")]
        [InlineData("unknown", true, "https://github.com/rygel/AIUsageTracker/releases/latest/download/appcast_beta_x64.xml")]
        public void GetAppcastUrl_ReturnsExpectedReleaseAsset(string architecture, bool isBeta, string expectedUrl)
        {
            var url = ReleaseUrlCatalog.GetAppcastUrl(architecture, isBeta);

            Assert.Equal(expectedUrl, url);
        }

        [Fact]
        public void ReleasePages_UseSharedRepositoryBase()
        {
            Assert.Equal("https://github.com/rygel/AIUsageTracker/releases", ReleaseUrlCatalog.GetReleasesPageUrl());
            Assert.Equal("https://github.com/rygel/AIUsageTracker/releases/latest", ReleaseUrlCatalog.GetLatestReleasePageUrl());
            Assert.Equal("https://github.com/rygel/AIUsageTracker/releases/tag/v2.2.19", ReleaseUrlCatalog.GetReleaseTagUrl("2.2.19"));
            Assert.Equal("https://api.github.com/repos/rygel/AIUsageTracker/releases/tags/v2.2.19", ReleaseUrlCatalog.GetGitHubReleaseApiUrl("2.2.19"));
        }
    }
}
