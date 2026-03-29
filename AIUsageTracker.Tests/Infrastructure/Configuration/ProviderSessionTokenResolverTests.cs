using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Configuration;

public class ProviderSessionTokenResolverTests
{
    [Fact]
    public async Task TryResolveAsync_ReadsAccessTokenFromDottedSchemaRootAsync()
    {
        var testRoot = TestTempPaths.CreateDirectory("provider-session-token-resolver");

        try
        {
            var authFilePath = Path.Combine(testRoot, "auth.json");
            var authContent = new
            {
                sessions = new
                {
                    github = new
                    {
                        user = "test-user",
                        oauth_token = "gho_test_token",
                    },
                },
            };

            await File.WriteAllTextAsync(authFilePath, JsonSerializer.Serialize(authContent));

            var pathProvider = new Mock<IAppPathProvider>();
            pathProvider.Setup(provider => provider.GetUserProfileRoot()).Returns(testRoot);

            var definition = new ProviderDefinition(
                "github-copilot",
                "GitHub Copilot",
                PlanType.Coding,
                true)
            {
                AuthIdentityCandidatePathTemplates = new[] { authFilePath },
                SessionAuthFileSchemas = new[]
                {
                    new ProviderAuthFileSchema("sessions.github", "oauth_token", "user"),
                },
            };

            var resolver = new ProviderSessionTokenResolver(
                definition.CreateAuthDiscoverySpec(),
                "GitHub auth session",
                "GitHub session",
                NullLogger<TokenDiscoveryService>.Instance,
                pathProvider.Object);

            var resolved = await resolver.TryResolveAsync();

            Assert.NotNull(resolved);
            Assert.Equal("github-copilot", resolved!.ProviderId);
            Assert.Equal("gho_test_token", resolved.ApiKey);
            Assert.Equal("GitHub auth session", resolved.Description);
            Assert.Contains(authFilePath, resolved.AuthSource, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestTempPaths.CleanupPath(testRoot);
        }
    }
}
