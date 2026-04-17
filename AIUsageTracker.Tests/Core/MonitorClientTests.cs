// <copyright file="MonitorClientTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using AIUsageTracker.Core.MonitorClient;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace AIUsageTracker.Tests.Core;

public class MonitorClientTests
{
    [Fact]
    public async Task MonitorService_GetConfigsAsync_ReturnsEmptyList_WhenMonitorNotAvailableAsync()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("http://localhost:9999"),
        };

        var service = new MonitorService(httpClient, NullLogger<MonitorService>.Instance);
        service.AgentUrl = "http://localhost:9999";

        // Act
        var configs = await service.GetConfigsAsync();

        // Assert
        Assert.NotNull(configs);
        Assert.Empty(configs);
    }

    [Fact]
    public async Task MonitorService_GetUsageAsync_ReturnsEmptyList_WhenMonitorNotAvailableAsync()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("http://localhost:9999"),
        };

        var service = new MonitorService(httpClient, NullLogger<MonitorService>.Instance);
        service.AgentUrl = "http://localhost:9999";

        // Act
        var usages = await service.GetUsageAsync();

        // Assert
        Assert.NotNull(usages);
        Assert.Empty(usages);
    }

    [Fact]
    public void MonitorLauncher_HasRequiredMethods()
    {
        var type = typeof(MonitorLauncher);

        Assert.NotNull(type.GetMethod("StartAgentAsync", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetMethod("StopAgentAsync", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetMethod("IsAgentRunningAsync", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(type.GetMethod("WaitForAgentAsync", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public async Task MonitorLauncher_GetAndValidateMonitorInfo_ReturnsNull_WhenHealthFailsAsync()
    {
        var launcher = new MonitorLauncher(
            monitorInfoCandidatePathsOverride: () => Array.Empty<string>(),
            healthCheckOverride: ignoredPort => Task.FromResult(false),
            processRunningOverride: ignoredProcessId => Task.FromResult(false));

        var result = await launcher.GetAndValidateMonitorInfoAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task MonitorLauncher_InvalidateMonitorInfo_DoesNotThrow_WhenFileMissingAsync()
    {
        var launcher = new MonitorLauncher();

        var exception = await Record.ExceptionAsync(() => launcher.InvalidateMonitorInfoAsync());
        Assert.Null(exception);
    }
}
