using AIUsageTracker.Core.Updates;
using Xunit;

namespace AIUsageTracker.Tests.Core;

public class ReleaseUrlCatalogTests
{
    [Theory]
    [InlineData("x64", false, "https://github.com/rygel/AIConsumptionTracker/releases/latest/download/appcast_x64.xml")]
    [InlineData("x86", false, "https://github.com/rygel/AIConsumptionTracker/releases/latest/download/appcast_x86.xml")]
    [InlineData("arm64", false, "https://github.com/rygel/AIConsumptionTracker/releases/latest/download/appcast_arm64.xml")]
    [InlineData("arm", true, "https://github.com/rygel/AIConsumptionTracker/releases/latest/download/appcast_beta_arm64.xml")]
    [InlineData("unknown", true, "https://github.com/rygel/AIConsumptionTracker/releases/latest/download/appcast_beta_x64.xml")]
    public void GetAppcastUrl_ReturnsExpectedReleaseAsset(string architecture, bool isBeta, string expectedUrl)
    {
        var url = ReleaseUrlCatalog.GetAppcastUrl(architecture, isBeta);

        Assert.Equal(expectedUrl, url);
    }

    [Fact]
    public void ReleasePages_UseSharedRepositoryBase()
    {
        Assert.Equal("https://github.com/rygel/AIConsumptionTracker/releases", ReleaseUrlCatalog.GetReleasesPageUrl());
        Assert.Equal("https://github.com/rygel/AIConsumptionTracker/releases/latest", ReleaseUrlCatalog.GetLatestReleasePageUrl());
        Assert.Equal("https://github.com/rygel/AIConsumptionTracker/releases/tag/v2.2.19", ReleaseUrlCatalog.GetReleaseTagUrl("2.2.19"));
        Assert.Equal("https://api.github.com/repos/rygel/AIConsumptionTracker/releases/tags/v2.2.19", ReleaseUrlCatalog.GetGitHubReleaseApiUrl("2.2.19"));
    }
}
