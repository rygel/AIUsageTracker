// <copyright file="ConfigServiceSaveValidationTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.Services;

public sealed class ConfigServiceSaveValidationTests : IntegrationTestBase
{
    private ConfigService CreateConfigService()
    {
        var authPath = this.CreateFile("config/auth.json", "{}");
        var providersPath = this.CreateFile("config/providers.json", "{}");
        var prefsPath = this.CreateFile("preferences.json", "{}");

        var mockPathProvider = new Mock<IAppPathProvider>();
        mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authPath);
        mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providersPath);
        mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(prefsPath);
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetAppDataRoot()).Returns(this.TestRootPath);
        mockPathProvider.Setup(p => p.GetDatabasePath()).Returns(Path.Combine(this.TestRootPath, "usage.db"));
        mockPathProvider.Setup(p => p.GetLogDirectory()).Returns(Path.Combine(this.TestRootPath, "logs"));

        return new ConfigService(
            NullLogger<ConfigService>.Instance,
            NullLoggerFactory.Instance,
            mockPathProvider.Object);
    }

    [Fact]
    public async Task SaveConfigAsync_UnknownProviderId_ThrowsArgumentExceptionAsync()
    {
        var service = this.CreateConfigService();
        var config = new ProviderConfig { ProviderId = "totally-unknown-provider-xyz-99999" };

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveConfigAsync(config));
    }

    [Fact]
    public async Task SaveConfigAsync_KnownProviderId_DoesNotThrowAsync()
    {
        var service = this.CreateConfigService();
        var config = new ProviderConfig { ProviderId = "claude-code", ApiKey = "sk-test" };

        var exception = await Record.ExceptionAsync(() => service.SaveConfigAsync(config));
        Assert.Null(exception);
    }

    [Fact]
    public async Task SaveConfigAsync_NullConfig_ThrowsArgumentNullExceptionAsync()
    {
        var service = this.CreateConfigService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SaveConfigAsync(null!));
    }

    [Fact]
    public async Task SaveConfigAsync_EmptyProviderId_ThrowsArgumentExceptionAsync()
    {
        var service = this.CreateConfigService();
        var config = new ProviderConfig { ProviderId = string.Empty };

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveConfigAsync(config));
    }
}
