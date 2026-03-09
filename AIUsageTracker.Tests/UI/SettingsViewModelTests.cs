// <copyright file="SettingsViewModelTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.UI
{
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Core.MonitorClient;
    using AIUsageTracker.UI.Slim.ViewModels;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Xunit;

    public class SettingsViewModelTests
    {
        private readonly Mock<IMonitorService> _monitorServiceMock;
        private readonly Mock<IUsageAnalyticsService> _analyticsServiceMock;
        private readonly Mock<IDataExportService> _exportServiceMock;
        private readonly Mock<ILogger<SettingsViewModel>> _loggerMock;
        private readonly SettingsViewModel _viewModel;

        public SettingsViewModelTests()
        {
            this._monitorServiceMock = new Mock<IMonitorService>();
            this._analyticsServiceMock = new Mock<IUsageAnalyticsService>();
            this._exportServiceMock = new Mock<IDataExportService>();
            this._loggerMock = new Mock<ILogger<SettingsViewModel>>();
            this._viewModel = new SettingsViewModel(
                this._monitorServiceMock.Object,
                this._analyticsServiceMock.Object,
                this._exportServiceMock.Object,
                this._loggerMock.Object);
        }

        [Fact]
        public void TogglePrivacyMode_UpdatesStateAndMessage()
        {
            // Arrange
            this._viewModel.IsPrivacyMode = false;

            // Act
            this._viewModel.TogglePrivacyMode();

            // Assert
            Assert.True(this._viewModel.IsPrivacyMode);
            Assert.Contains("Enabled", this._viewModel.StatusMessage);

            // Act again
            this._viewModel.TogglePrivacyMode();

            // Assert
            Assert.False(this._viewModel.IsPrivacyMode);
            Assert.Contains("Disabled", this._viewModel.StatusMessage);
        }

        [Fact]
        public async Task LoadDataAsync_PopulatesConfigs_WhenSuccessful()
        {
            // Arrange
            var testConfigs = new List<ProviderConfig>
            {
                new ProviderConfig { ProviderId = "codex", ApiKey = "key1" },
                new ProviderConfig { ProviderId = "claude-code", ApiKey = "key2" }
            };
            this._monitorServiceMock.Setup(m => m.GetConfigsAsync()).ReturnsAsync(testConfigs);
            this._monitorServiceMock.Setup(m => m.GetUsageAsync()).ReturnsAsync(new List<ProviderUsage>());

            // Act
            await this._viewModel.LoadDataAsync();

            // Assert
            Assert.Equal(2, this._viewModel.Configs.Count);
            Assert.Equal("Loaded 2 providers.", this._viewModel.StatusMessage);
            Assert.False(this._viewModel.IsLoading);
        }

        [Fact]
        public async Task LoadDataAsync_SetsErrorMessage_WhenServiceFails()
        {
            // Arrange
            this._monitorServiceMock.Setup(m => m.GetConfigsAsync()).ThrowsAsync(new Exception("Network error"));

            // Act
            await this._viewModel.LoadDataAsync();

            // Assert
            Assert.Equal("Error loading settings.", this._viewModel.StatusMessage);
            Assert.False(this._viewModel.IsLoading);
        }
    }
}
