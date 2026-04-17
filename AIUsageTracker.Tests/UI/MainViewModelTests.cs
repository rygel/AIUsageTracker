// <copyright file="MainViewModelTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim.Services;
using AIUsageTracker.UI.Slim.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Tests.UI;

public class MainViewModelTests
{
    private readonly Mock<IMonitorService> _monitorServiceMock;
    private readonly Mock<IBrowserService> _browserServiceMock;
    private readonly Mock<IDialogService> _dialogServiceMock;
    private readonly Mock<ILogger<MainViewModel>> _loggerMock;
    private readonly MainViewModel _viewModel;

    public MainViewModelTests()
    {
        this._monitorServiceMock = new Mock<IMonitorService>();
        this._browserServiceMock = new Mock<IBrowserService>();
        this._dialogServiceMock = new Mock<IDialogService>();
        this._loggerMock = new Mock<ILogger<MainViewModel>>();
        this._viewModel = new MainViewModel(
            this._monitorServiceMock.Object,
            this._loggerMock.Object,
            this._browserServiceMock.Object,
            this._dialogServiceMock.Object);
    }

    [Fact]
    public void InitialState_IsCorrect()
    {
        Assert.Equal("Initializing...", this._viewModel.StatusMessage);
        Assert.False(this._viewModel.IsLoading);
        Assert.Empty(this._viewModel.Usages);
    }

    [Fact]
    public void SetPrivacyMode_UpdatesState()
    {
        this._viewModel.SetPrivacyMode(true);
        Assert.True(this._viewModel.IsPrivacyMode);

        this._viewModel.SetPrivacyMode(false);
        Assert.False(this._viewModel.IsPrivacyMode);
    }

    [Fact]
    public async Task RefreshDataAsync_PopulatesUsages_WhenSuccessfulAsync()
    {
        // Arrange
        var testUsages = new List<ProviderUsage>
        {
            new ProviderUsage { ProviderId = "p1", ProviderName = "P1" },
            new ProviderUsage { ProviderId = "p2", ProviderName = "P2" },
        };
        this._monitorServiceMock.Setup(m => m.GetUsageAsync()).ReturnsAsync(testUsages);

        // Act
        await this._viewModel.RefreshDataAsync();

        // Assert
        Assert.Equal(2, this._viewModel.Usages.Count);
        Assert.Equal("Data updated", this._viewModel.StatusMessage);
        Assert.NotEqual(DateTime.MinValue, this._viewModel.LastRefreshTime);
        Assert.False(this._viewModel.IsLoading);
    }

    [Fact]
    public async Task RefreshDataAsync_SetsErrorMessage_WhenServiceFailsAsync()
    {
        // Arrange
        this._monitorServiceMock.Setup(m => m.GetUsageAsync()).ThrowsAsync(new Exception("API Down"));

        // Act
        await this._viewModel.RefreshDataAsync();

        // Assert
        Assert.Equal("Connection failed", this._viewModel.StatusMessage);
        Assert.Empty(this._viewModel.Usages);
        Assert.False(this._viewModel.IsLoading);
    }

    [Fact]
    public async Task RefreshDataAsync_HandlesEmptyResultsAsync()
    {
        // Arrange
        this._monitorServiceMock.Setup(m => m.GetUsageAsync()).ReturnsAsync(new List<ProviderUsage>());

        // Act
        await this._viewModel.RefreshDataAsync();

        // Assert
        Assert.Equal("No active providers found", this._viewModel.StatusMessage);
        Assert.Empty(this._viewModel.Usages);
    }
}
