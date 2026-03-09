// <copyright file="PrivacyHelperTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Infrastructure
{
    using AIUsageTracker.Infrastructure.Helpers;
    using Xunit;

    public class PrivacyHelperTests
    {
        [Theory]
        [InlineData("test@example.com", null, "t**t@*******.***")]
        [InlineData("john.doe@example.com", null, "j*****e@*******.***")]
        [InlineData("johndoe", "johndoe", "j*****e")]
        [InlineData("abc", "abc", "a*c")]
        [InlineData("ab", "ab", "**")]
        [InlineData("a", "a", "*")]
        [InlineData(string.Empty, null, string.Empty)]
        [InlineData(null, null, null)]
        public void MaskContent_ShouldMaskCorrectly(string? input, string? accountName, string? expected)
        {
            var result = PrivacyHelper.MaskContent(input ?? string.Empty, accountName);
            Assert.Equal(expected ?? string.Empty, result);
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
            Assert.Equal("Usage for t**t@*******.*** is 50", result);
        }

        [Fact]
        public void MaskPath_ShouldObfuscateUserProfile()
        {
            // Arrange
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userName = System.IO.Path.GetFileName(userProfile);
            var path = System.IO.Path.Combine(userProfile, ".ai-consumption-tracker", "auth.json");

            // Act
            var maskedPath = PrivacyHelper.MaskPath(path);

            // Assert
            Assert.Contains(".ai-consumption-tracker", maskedPath);
            Assert.Contains("auth.json", maskedPath);
            Assert.DoesNotContain(userName, maskedPath); // Should NOT contain the real username
        }

        [Fact]
        public void MaskPath_ShouldPreserveDriveAndFilename_WhenNotInUserProfile()
        {
            // Arrange
            var path = @"C:\Temp\Secret\config.json";

            // Act
            var maskedPath = PrivacyHelper.MaskPath(path);

            // Assert
            Assert.Contains("config.json", maskedPath);
            Assert.DoesNotContain("Secret", maskedPath);
        }
    }
}
