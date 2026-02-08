using Xunit;
using AIConsumptionTracker.Infrastructure.Services;

namespace AIConsumptionTracker.Tests.Infrastructure;

public class GitHubUpdateCheckerTests
{
    [Fact]
    public void IsUpdateAvailable_ReturnsFalse_WhenTagIsSameAsCurrent()
    {
        // Arrange
        var current = new Version(1, 0, 0);
        var tag = "v1.0.0";

        // Act
        bool result = GitHubUpdateChecker.IsUpdateAvailable(current, tag, out var parsed);

        // Assert
        Assert.False(result);
        Assert.Equal(current, parsed);
    }

    [Fact]
    public void IsUpdateAvailable_ReturnsTrue_WhenTagIsNewer()
    {
        // Arrange
        var current = new Version(1, 0, 0);
        var tag = "v1.0.1";

        // Act
        bool result = GitHubUpdateChecker.IsUpdateAvailable(current, tag, out var parsed);

        // Assert
        Assert.True(result);
        Assert.Equal(new Version(1, 0, 1), parsed);
    }

    [Fact]
    public void IsUpdateAvailable_ReturnsFalse_WhenTagIsOlder()
    {
        // Arrange
        var current = new Version(1, 0, 1);
        var tag = "v1.0.0";

        // Act
        bool result = GitHubUpdateChecker.IsUpdateAvailable(current, tag, out var parsed);

        // Assert
        Assert.False(result);
        Assert.Equal(new Version(1, 0, 0), parsed);
    }

    [Fact]
    public void IsUpdateAvailable_HandlesTagWithoutV()
    {
        // Arrange
        var current = new Version(1, 0, 0);
        var tag = "1.0.1";

        // Act
        bool result = GitHubUpdateChecker.IsUpdateAvailable(current, tag, out var parsed);

        // Assert
        Assert.True(result);
        Assert.Equal(new Version(1, 0, 1), parsed);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    [InlineData("v")]
    public void IsUpdateAvailable_ReturnsFalse_OnInvalidTag(string invalidTag)
    {
        var current = new Version(1, 0, 0);
        bool result = GitHubUpdateChecker.IsUpdateAvailable(current, invalidTag, out var parsed);
        Assert.False(result);
        Assert.Null(parsed);
    }
}
