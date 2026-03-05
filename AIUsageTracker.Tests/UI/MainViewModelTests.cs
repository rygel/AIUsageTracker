using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.UI.Slim.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIUsageTracker.Tests.UI;

public class MainViewModelTests
{
    private readonly Mock<IMonitorService> _monitorServiceMock;
    private readonly Mock<ILogger<MainViewModel>> _loggerMock;
    private readonly MainViewModel _viewModel;

    public MainViewModelTests()
    {
        _monitorServiceMock = new Mock<IMonitorService>();
        _loggerMock = new Mock<ILogger<MainViewModel>>();
        _viewModel = new MainViewModel(_monitorServiceMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void InitialState_IsCorrect()
    {
        Assert.Equal("Initializing...", _viewModel.StatusMessage);
        Assert.False(_viewModel.IsLoading);
        Assert.Empty(_viewModel.Usages);
    }

    [Fact]
    public void SetPrivacyMode_UpdatesState()
    {
        _viewModel.SetPrivacyMode(true);
        Assert.True(_viewModel.IsPrivacyMode);

        _viewModel.SetPrivacyMode(false);
        Assert.False(_viewModel.IsPrivacyMode);
    }

    [Fact]
    public async Task RefreshDataAsync_PopulatesUsages_WhenSuccessful()
    {
        // Arrange
        var testUsages = new List<ProviderUsage>
        {
            new ProviderUsage { ProviderId = "p1", ProviderName = "P1" },
            new ProviderUsage { ProviderId = "p2", ProviderName = "P2" }
        };
        _monitorServiceMock.Setup(m => m.GetUsageAsync()).ReturnsAsync(testUsages);

        // Act
        await _viewModel.RefreshDataAsync();

        // Assert
        Assert.Equal(2, _viewModel.Usages.Count);
        Assert.Equal("Data updated", _viewModel.StatusMessage);
        Assert.NotEqual(DateTime.MinValue, _viewModel.LastRefreshTime);
        Assert.False(_viewModel.IsLoading);
    }

    [Fact]
    public async Task RefreshDataAsync_SetsErrorMessage_WhenServiceFails()
    {
        // Arrange
        _monitorServiceMock.Setup(m => m.GetUsageAsync()).ThrowsAsync(new Exception("API Down"));

        // Act
        await _viewModel.RefreshDataAsync();

        // Assert
        Assert.Equal("Connection failed", _viewModel.StatusMessage);
        Assert.Empty(_viewModel.Usages);
        Assert.False(_viewModel.IsLoading);
    }

    [Fact]
    public async Task RefreshDataAsync_HandlesEmptyResults()
    {
        // Arrange
        _monitorServiceMock.Setup(m => m.GetUsageAsync()).ReturnsAsync(new List<ProviderUsage>());

        // Act
        await _viewModel.RefreshDataAsync();

        // Assert
        Assert.Equal("No active providers found", _viewModel.StatusMessage);
        Assert.Empty(_viewModel.Usages);
    }
}
