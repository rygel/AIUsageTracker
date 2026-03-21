// <copyright file="MainViewModelTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim.Services;
using AIUsageTracker.UI.Slim.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.UI.ViewModels;

/// <summary>
/// Tests for the MainViewModel.
/// </summary>
public class MainViewModelTests
{
    private readonly Mock<IMonitorService> _mockMonitorService;
    private readonly Mock<IBrowserService> _mockBrowserService;
    private readonly Mock<IDialogService> _mockDialogService;
    private readonly ILogger<MainViewModel> _logger;

    public MainViewModelTests()
    {
        this._mockMonitorService = new Mock<IMonitorService>();
        this._mockBrowserService = new Mock<IBrowserService>();
        this._mockDialogService = new Mock<IDialogService>();
        this._logger = NullLogger<MainViewModel>.Instance;

        // Default setup for RefreshPortAsync which is called before GetUsageAsync
        this._mockMonitorService
            .Setup(m => m.RefreshPortAsync())
            .Returns(Task.CompletedTask);
    }

    private MainViewModel CreateViewModel(
        IBrowserService? browserService = null,
        IDialogService? dialogService = null)
    {
        return new MainViewModel(
            this._mockMonitorService.Object,
            this._logger,
            browserService ?? this._mockBrowserService.Object,
            dialogService ?? this._mockDialogService.Object);
    }

    [Fact]
    public void Constructor_InitializesDefaultValues()
    {
        // Act
        var viewModel = this.CreateViewModel();

        // Assert
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.IsPrivacyMode);
        Assert.Equal("Initializing...", viewModel.StatusMessage);
        Assert.NotNull(viewModel.Usages);
        Assert.NotNull(viewModel.Sections);
    }

    [Fact]
    public async Task RefreshDataCommand_SetsIsLoadingTrue_WhileRefreshingAsync()
    {
        // Arrange
        var isLoadingValues = new List<bool>();
        this._mockMonitorService
            .Setup(m => m.GetUsageAsync())
            .ReturnsAsync(new List<ProviderUsage>());

        var viewModel = this.CreateViewModel();
        viewModel.PropertyChanged += (s, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(viewModel.IsLoading), StringComparison.Ordinal))
            {
                isLoadingValues.Add(viewModel.IsLoading);
            }
        };

        // Act
        await viewModel.RefreshDataCommand.ExecuteAsync(null);

        // Assert
        Assert.Contains(true, isLoadingValues); // Was true during loading
        Assert.False(viewModel.IsLoading); // Is false after completion
    }

    [Fact]
    public async Task RefreshDataCommand_UpdatesUsages_OnSuccessAsync()
    {
        // Arrange
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "provider1" },
            new() { ProviderId = "provider2" },
        };
        this._mockMonitorService
            .Setup(m => m.GetUsageAsync())
            .ReturnsAsync(usages);

        var viewModel = this.CreateViewModel();

        // Act
        await viewModel.RefreshDataCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(2, viewModel.Usages.Count);
    }

    [Fact]
    public async Task RefreshDataCommand_UpdatesStatusMessage_OnSuccessAsync()
    {
        // Arrange
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "provider1" },
        };
        this._mockMonitorService
            .Setup(m => m.GetUsageAsync())
            .ReturnsAsync(usages);

        var viewModel = this.CreateViewModel();

        // Act
        await viewModel.RefreshDataCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Data updated", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RefreshDataCommand_UpdatesStatusMessage_WhenNoProvidersAsync()
    {
        // Arrange
        this._mockMonitorService
            .Setup(m => m.GetUsageAsync())
            .ReturnsAsync(new List<ProviderUsage>());

        var viewModel = this.CreateViewModel();

        // Act
        await viewModel.RefreshDataCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("No active providers found", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RefreshDataCommand_UpdatesStatusMessage_OnErrorAsync()
    {
        // Arrange
        this._mockMonitorService
            .Setup(m => m.GetUsageAsync())
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        var viewModel = this.CreateViewModel();

        // Act
        await viewModel.RefreshDataCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Connection failed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RefreshDataCommand_UpdatesLastRefreshTimeAsync()
    {
        // Arrange
        this._mockMonitorService
            .Setup(m => m.GetUsageAsync())
            .ReturnsAsync(new List<ProviderUsage>());

        var viewModel = this.CreateViewModel();
        var beforeRefresh = DateTime.Now;

        // Act
        await viewModel.RefreshDataCommand.ExecuteAsync(null);

        // Assert
        Assert.True(viewModel.LastRefreshTime >= beforeRefresh);
    }

    [Fact]
    public async Task RefreshDataCommand_DoesNotRunConcurrentlyAsync()
    {
        // Arrange
        var callCount = 0;
        var tcs = new TaskCompletionSource<IReadOnlyList<ProviderUsage>>();

#pragma warning disable VSTHRD003 // Test intentionally returns externally-controlled TaskCompletionSource task.
        this._mockMonitorService
            .Setup(m => m.GetUsageAsync())
            .Returns(() =>
            {
                callCount++;
                return tcs.Task;
            });
#pragma warning restore VSTHRD003

        var viewModel = this.CreateViewModel();

        // Act - start first refresh
        var task1 = viewModel.RefreshDataCommand.ExecuteAsync(null);

        // Try to start second refresh while first is running
        var task2 = viewModel.RefreshDataCommand.ExecuteAsync(null);

        // Complete the first task
        tcs.SetResult(new List<ProviderUsage>());
        await Task.WhenAll(task1, task2);

        // Assert - should only have called once
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void TogglePrivacyModeCommand_TogglesIsPrivacyMode()
    {
        // Arrange
        var viewModel = this.CreateViewModel();
        var initialState = viewModel.IsPrivacyMode;

        // Act
        viewModel.TogglePrivacyModeCommand.Execute(null);

        // Assert
        Assert.NotEqual(initialState, viewModel.IsPrivacyMode);
    }

    [Fact]
    public void SetPrivacyMode_UpdatesIsPrivacyMode()
    {
        // Arrange
        var viewModel = this.CreateViewModel();

        // Act
        viewModel.SetPrivacyMode(true);

        // Assert
        Assert.True(viewModel.IsPrivacyMode);
    }

    [Fact]
    public void UpdateSections_CreatesQuotaAndPaygoSections()
    {
        // Arrange
        var viewModel = this.CreateViewModel();

        // Use valid provider IDs from ProviderMetadataCatalog
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", IsQuotaBased = true, IsAvailable = true },
            new() { ProviderId = "antigravity", IsQuotaBased = false, IsAvailable = true },
        };
        var prefs = new AppPreferences
        {
            ShowUsedPercentages = false,
            ColorThresholdYellow = 60,
            ColorThresholdRed = 80,
        };

        // Act
        viewModel.UpdateSections(usages, prefs);

        // Assert
        Assert.Equal(2, viewModel.Sections.Count);
    }

    [Fact]
    public void UpdateSections_OnlyCreatesQuotaSection_WhenNoPaygoProviders()
    {
        // Arrange
        var viewModel = this.CreateViewModel();

        // Use valid provider ID from ProviderMetadataCatalog
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", IsQuotaBased = true, IsAvailable = true },
        };
        var prefs = new AppPreferences();

        // Act
        viewModel.UpdateSections(usages, prefs);

        // Assert
        Assert.Single(viewModel.Sections);
    }

    [Fact]
    public void UpdateSections_SetsShowUsedPercentages()
    {
        // Arrange
        var viewModel = this.CreateViewModel();
        var prefs = new AppPreferences { ShowUsedPercentages = true };

        // Act
        viewModel.UpdateSections(new List<ProviderUsage>(), prefs);

        // Assert
        Assert.True(viewModel.ShowUsedPercentages);
    }

    [Fact]
    public void OnIsPrivacyModeChanged_UpdatesSectionCards()
    {
        // Arrange
        var viewModel = this.CreateViewModel();

        // Use valid provider ID from ProviderMetadataCatalog
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", IsQuotaBased = true, AccountName = "user@test.com", IsAvailable = true },
        };
        var prefs = new AppPreferences();
        viewModel.UpdateSections(usages, prefs);

        // Act
        viewModel.SetPrivacyMode(true);

        // Assert - all cards should have privacy mode set
        foreach (var section in viewModel.Sections)
        {
            foreach (var card in section.Items)
            {
                Assert.True(card.IsPrivacyMode);
            }
        }
    }

    [Fact]
    public void OnShowUsedPercentagesChanged_UpdatesSectionCards()
    {
        // Arrange
        var viewModel = this.CreateViewModel();

        // Use valid provider ID from ProviderMetadataCatalog
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", IsQuotaBased = true, IsAvailable = true },
        };
        var prefs = new AppPreferences { ShowUsedPercentages = false };
        viewModel.UpdateSections(usages, prefs);

        // Act
        viewModel.ShowUsedPercentages = true;

        // Assert - all cards should have ShowUsedPercentages set
        foreach (var section in viewModel.Sections)
        {
            foreach (var card in section.Items)
            {
                Assert.True(card.ShowUsedPercentages);
            }
        }
    }

    [Fact]
    public async Task OpenWebUICommand_CallsBrowserServiceAsync()
    {
        // Arrange
        var mockBrowserService = new Mock<IBrowserService>();
        mockBrowserService.Setup(b => b.OpenWebUIAsync()).Returns(Task.CompletedTask);

        var viewModel = this.CreateViewModel(browserService: mockBrowserService.Object);

        // Act
        await viewModel.OpenWebUICommand.ExecuteAsync(null);

        // Assert
        mockBrowserService.Verify(b => b.OpenWebUIAsync(), Times.Once);
    }

    [Fact]
    public void ViewChangelogCommand_CallsBrowserService()
    {
        // Arrange
        var mockBrowserService = new Mock<IBrowserService>();

        var viewModel = this.CreateViewModel(browserService: mockBrowserService.Object);

        // Act
        viewModel.ViewChangelogCommand.Execute(null);

        // Assert
        mockBrowserService.Verify(b => b.OpenReleasesPage(), Times.Once);
    }

    [Fact]
    public async Task OpenSettingsCommand_CallsDialogServiceAsync()
    {
        // Arrange
        var mockDialogService = new Mock<IDialogService>();
        mockDialogService.Setup(d => d.ShowSettingsAsync(null)).ReturnsAsync(false);

        var viewModel = this.CreateViewModel(dialogService: mockDialogService.Object);

        // Act
        await viewModel.OpenSettingsCommand.ExecuteAsync(null);

        // Assert
        mockDialogService.Verify(d => d.ShowSettingsAsync(null), Times.Once);
    }

    [Fact]
    public async Task OpenSettingsCommand_RefreshesData_WhenSettingsChangedAsync()
    {
        // Arrange
        var mockDialogService = new Mock<IDialogService>();
        mockDialogService.Setup(d => d.ShowSettingsAsync(null)).ReturnsAsync(true);

        this._mockMonitorService
            .Setup(m => m.GetUsageAsync())
            .ReturnsAsync(new List<ProviderUsage>());

        var viewModel = this.CreateViewModel(dialogService: mockDialogService.Object);

        // Act
        await viewModel.OpenSettingsCommand.ExecuteAsync(null);

        // Assert - should have called GetUsageAsync for refresh
        this._mockMonitorService.Verify(m => m.GetUsageAsync(), Times.Once);
    }
}
