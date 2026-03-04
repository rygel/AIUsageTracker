using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class OpenCodeZenProviderTests
{
    [Fact]
    public async Task GetUsageAsync_WhenCliMissing_PopulatesRawSnapshotFields()
    {
        var logger = new Mock<ILogger<OpenCodeZenProvider>>();
        var provider = new OpenCodeZenProvider(logger.Object, @"C:\__missing__\opencode.cmd");
        var config = new ProviderConfig { ProviderId = "opencode-zen", ApiKey = "not-required" };

        var usage = (await provider.GetUsageAsync(config)).Single();

        Assert.False(usage.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(usage.RawJson));
        Assert.Equal(404, usage.HttpStatus);
    }
}
