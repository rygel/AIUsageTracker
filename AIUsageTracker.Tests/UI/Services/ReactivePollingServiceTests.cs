// <copyright file="ReactivePollingServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reactive.Linq;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.UI.Services;

/// <summary>
/// Tests for the ReactivePollingService.
/// </summary>
public class ReactivePollingServiceTests : IDisposable
{
    private readonly Mock<IMonitorService> _mockMonitorService;
    private readonly ILogger<ReactivePollingService> _logger;
    private readonly ReactivePollingService _service;
    private bool _disposed;

    public ReactivePollingServiceTests()
    {
        this._mockMonitorService = new Mock<IMonitorService>();
        this._logger = NullLogger<ReactivePollingService>.Instance;
        this._service = new ReactivePollingService(
            this._mockMonitorService.Object,
            this._logger);
    }

    [Fact]
    public void Constructor_InitializesWithDefaultInterval()
    {
        // Assert
        Assert.Equal(TimeSpan.FromMinutes(1), this._service.Interval);
        Assert.False(this._service.IsPolling);
    }

    [Fact]
    public void Interval_CanBeChanged()
    {
        // Act
        this._service.Interval = TimeSpan.FromSeconds(30);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), this._service.Interval);
    }

    [Fact]
    public void Start_SetsIsPollingToTrue()
    {
        // Act
        this._service.Start();

        // Assert
        Assert.True(this._service.IsPolling);
    }

    [Fact]
    public void Stop_SetsIsPollingToFalse()
    {
        // Arrange
        this._service.Start();

        // Act
        this._service.Stop();

        // Assert
        Assert.False(this._service.IsPolling);
    }

    [Fact]
    public async Task RefreshNowAsync_CallsMonitorServiceAsync()
    {
        // Arrange
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "test-provider" },
        };
        this._mockMonitorService
            .Setup(m => m.GetUsageAsync())
            .ReturnsAsync(usages);

        // Act
        await this._service.RefreshNowAsync();

        // Assert
        this._mockMonitorService.Verify(m => m.GetUsageAsync(), Times.Once);
    }

    [Fact]
    public async Task RefreshNowAsync_EmitsToUsageStreamAsync()
    {
        // Arrange
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "test-provider" },
        };
        this._mockMonitorService
            .Setup(m => m.GetUsageAsync())
            .ReturnsAsync(usages);

        IReadOnlyList<ProviderUsage>? receivedUsages = null;
        using var subscription = this._service.UsageStream.Subscribe(u => receivedUsages = u);

        // Act
        await this._service.RefreshNowAsync();

        // Assert
        Assert.NotNull(receivedUsages);
        Assert.Single(receivedUsages);
        Assert.Equal("test-provider", receivedUsages[0].ProviderId);
    }

    [Fact]
    public async Task RefreshNowAsync_EmitsToErrorStream_OnExceptionAsync()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Test error");
        this._mockMonitorService
            .Setup(m => m.GetUsageAsync())
            .ThrowsAsync(expectedException);

        Exception? receivedException = null;
        using var subscription = this._service.ErrorStream.Subscribe(e => receivedException = e);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => this._service.RefreshNowAsync());
        Assert.NotNull(receivedException);
        Assert.Equal("Test error", receivedException.Message);
    }

    [Fact]
    public void UsageStream_IsObservable()
    {
        // Assert
        Assert.NotNull(this._service.UsageStream);
        Assert.IsAssignableFrom<IObservable<IReadOnlyList<ProviderUsage>>>(this._service.UsageStream);
    }

    [Fact]
    public void ErrorStream_IsObservable()
    {
        // Assert
        Assert.NotNull(this._service.ErrorStream);
        Assert.IsAssignableFrom<IObservable<Exception>>(this._service.ErrorStream);
    }

    [Fact]
    public void Dispose_StopsPolling()
    {
        // Arrange
        this._service.Start();
        Assert.True(this._service.IsPolling);

        // Act
        this._service.Dispose();

        // Assert
        Assert.False(this._service.IsPolling);
    }

    [Fact]
    public void Start_DoesNotStartTwice()
    {
        // Arrange
        this._service.Start();

        // Act - try to start again
        this._service.Start();

        // Assert - should still be polling (no exception)
        Assert.True(this._service.IsPolling);
    }

    [Fact]
    public async Task CreatePollingObservable_ReturnsObservableOfUsagesAsync()
    {
        // Arrange
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "test-provider" },
        };
        this._mockMonitorService
            .Setup(m => m.GetUsageAsync())
            .ReturnsAsync(usages);

        // Act
        var observable = this._service.CreatePollingObservable(TimeSpan.FromMilliseconds(100));

        // Assert
        Assert.NotNull(observable);

        // Take first emission
        var result = await observable.Take(1).ToList();
        Assert.Single(result);
        Assert.Single(result[0]);
    }

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (this._disposed)
        {
            return;
        }

        if (disposing)
        {
            this._service.Dispose();
        }

        this._disposed = true;
    }
}
