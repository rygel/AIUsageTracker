using Xunit;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Mappers;
using NetSparkleUpdater;

namespace AIUsageTracker.Tests.Infrastructure;

public class UpdateMapperTests
{
    [Fact]
    public void ToAppCastItem_ShouldCorrectlyMapFields()
    {
        // Arrange
        var now = DateTime.Now;
        var info = new AIUsageTracker.Core.Interfaces.UpdateInfo
        {
            Version = "v1.7.14",
            DownloadUrl = "https://example.com/download.exe",
            ReleaseUrl = "https://example.com/release",
            ReleaseNotes = "New features",
            PublishedAt = now
        };

        // Act
        var item = UpdateMapper.ToAppCastItem(info);

        // Assert
        Assert.Equal("1.7.14", item.Version); // Should strip 'v'
        Assert.Equal(info.DownloadUrl, item.DownloadLink);
        Assert.Equal(info.ReleaseUrl, item.ReleaseNotesLink);
        Assert.Equal(info.PublishedAt, item.PublicationDate);
        Assert.False(item.IsCriticalUpdate);
    }

    [Fact]
    public void ToAppCastItem_ShouldHandleVersionWithoutV()
    {
        // Arrange
        var info = new AIUsageTracker.Core.Interfaces.UpdateInfo
        {
            Version = "1.7.14",
            DownloadUrl = "https://example.com/download.exe"
        };

        // Act
        var item = UpdateMapper.ToAppCastItem(info);

        // Assert
        Assert.Equal("1.7.14", item.Version);
    }
}

