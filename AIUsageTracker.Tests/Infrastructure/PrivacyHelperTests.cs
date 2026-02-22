using System;
using AIUsageTracker.Infrastructure.Helpers;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure
{
    public class PrivacyHelperTests
    {
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

