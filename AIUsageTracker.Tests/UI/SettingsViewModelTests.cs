using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.UI.Slim.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIUsageTracker.Tests.UI;

public class SettingsViewModelTests
{
    private readonly Mock<IMonitorService> _monitorServiceMock;
    private readonly Mock<IUsageAnalyticsService> _analyticsServiceMock;
    private readonly Mock<IDataExportService> _exportServiceMock;
    private readonly Mock<ILogger<SettingsViewModel>> _loggerMock;
    private readonly SettingsViewModel _viewModel;

    public SettingsViewModelTests()
    {
        _monitorServiceMock = new Mock<IMonitorService>();
        _analyticsServiceMock = new Mock<IUsageAnalyticsService>();
        _exportServiceMock = new Mock<IDataExportService>();
        _loggerMock = new Mock<ILogger<SettingsViewModel>>();
        _viewModel = new SettingsViewModel(
            _monitorServiceMock.Object, 
            _analyticsServiceMock.Object,
            _exportServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public void TogglePrivacyMode_UpdatesStateAndMessage()
    {
        // Arrange
        _viewModel.IsPrivacyMode = false;

        // Act
        _viewModel.TogglePrivacyMode();

        // Assert
        Assert.True(_viewModel.IsPrivacyMode);
        Assert.Contains("Enabled", _viewModel.StatusMessage);

        // Act again
        _viewModel.TogglePrivacyMode();

        // Assert
        Assert.False(_viewModel.IsPrivacyMode);
        Assert.Contains("Disabled", _viewModel.StatusMessage);
    }

    [Fact]
    public async Task LoadDataAsync_PopulatesConfigs_WhenSuccessful()
    {
        // Arrange
        var testConfigs = new List<ProviderConfig>
        {
            new ProviderConfig { ProviderId = "openai", ApiKey = "key1" },
            new ProviderConfig { ProviderId = "anthropic", ApiKey = "key2" }
        };
        _monitorServiceMock.Setup(m => m.GetConfigsAsync()).ReturnsAsync(testConfigs);
        _monitorServiceMock.Setup(m => m.GetUsageAsync()).ReturnsAsync(new List<ProviderUsage>());

        // Act
        await _viewModel.LoadDataAsync();

        // Assert
        Assert.Equal(2, _viewModel.Configs.Count);
        Assert.Equal("Loaded 2 providers.", _viewModel.StatusMessage);
        Assert.False(_viewModel.IsLoading);
    }

    [Fact]
    public async Task LoadDataAsync_SetsErrorMessage_WhenServiceFails()
    {
        // Arrange
        _monitorServiceMock.Setup(m => m.GetConfigsAsync()).ThrowsAsync(new Exception("Network error"));

        // Act
        await _viewModel.LoadDataAsync();

        // Assert
        Assert.Equal("Error loading settings.", _viewModel.StatusMessage);
        Assert.False(_viewModel.IsLoading);
    }
}