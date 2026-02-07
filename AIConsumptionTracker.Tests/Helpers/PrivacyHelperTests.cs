using AIConsumptionTracker.Infrastructure.Helpers;
using Xunit;

namespace AIConsumptionTracker.Tests.Helpers;

public class PrivacyHelperTests
{
    [Theory]
    [InlineData("test@example.com", null, "t**t@example.com")]
    [InlineData("alexander.brandt@gmail.com", null, "a*****t@gmail.com")]
    [InlineData("johndoe", "johndoe", "j*****e")]
    [InlineData("abc", "abc", "a*c")]
    [InlineData("ab", "ab", "**")]
    [InlineData("a", "a", "*")]
    [InlineData("", null, "")]
    [InlineData(null, null, null)]
    public void MaskContent_ShouldMaskCorrectly(string? input, string? accountName, string? expected)
    {
        var result = PrivacyHelper.MaskContent(input ?? "", accountName);
        Assert.Equal(expected ?? "", result);
    }

    [Fact]
    public void MaskContent_ShouldMaskSurgically()
    {
        var input = "Logged in as johndoe";
        var result = PrivacyHelper.MaskContent(input, "johndoe");
        Assert.Equal("Logged in as j*****e", result);
    }

    [Fact]
    public void MaskContent_ShouldMaskEmailInsideString()
    {
        var input = "Usage for test@example.com is 50";
        var result = PrivacyHelper.MaskContent(input);
        Assert.Equal("Usage for t**t@example.com is 50", result);
    }
}
