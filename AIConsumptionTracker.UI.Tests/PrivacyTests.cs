using System.Windows;
using System.Windows.Controls;
using AIConsumptionTracker.UI;
using AIConsumptionTracker.Core.Services;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Infrastructure.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AIConsumptionTracker.UI.Tests;

public class PrivacyTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Mock<IConfigLoader> _mockConfigLoader;
    private readonly Mock<IProviderService> _mockProvider;

    public PrivacyTests()
    {
        var services = new ServiceCollection();
        
        _mockConfigLoader = new Mock<IConfigLoader>();
        _mockConfigLoader.Setup(c => c.LoadPreferencesAsync()).ReturnsAsync(new AppPreferences { ShowAll = true });
        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(new List<ProviderConfig>());

        _mockProvider = new Mock<IProviderService>();
        _mockProvider.Setup(p => p.ProviderId).Returns("test-provider");
        _mockProvider.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>()))
            .ReturnsAsync(new[] { new ProviderUsage 
            { 
                ProviderId = "test-provider",
                ProviderName = "Test Provider",
                AccountName = "test@example.com",
                Description = "Usage for test@example.com is 50",
                IsAvailable = true,
                PaymentType = PaymentType.Quota
            }});
        
        var providers = new List<IProviderService> { _mockProvider.Object };
        var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<ProviderManager>>();
        var providerManager = new ProviderManager(providers, _mockConfigLoader.Object, mockLogger.Object);
        
        var mockFontProvider = new Mock<IFontProvider>();
        var mockGithubAuth = new Mock<IGitHubAuthService>();
        
        services.AddSingleton(_mockConfigLoader.Object);
        services.AddSingleton(providerManager);
        services.AddSingleton(mockFontProvider.Object);
        services.AddSingleton(mockGithubAuth.Object);
        services.AddTransient<MainWindow>();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    [WpfFact]
    public async Task PrivacyMode_ShouldMaskAccountNameAndDescription_StandardMode()
    {
        // Arrange
        // ProviderManager auto-adds "github-copilot", so we use that ID
        var providerId = "github-copilot";
        _mockConfigLoader.Setup(c => c.LoadPreferencesAsync()).ReturnsAsync(new AppPreferences { ShowAll = true, CompactMode = false });
        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(new List<ProviderConfig> { 
            new ProviderConfig { ProviderId = providerId, ApiKey = "fake-key" }
        });

        _mockProvider.Setup(p => p.ProviderId).Returns(providerId);
        _mockProvider.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>()))
            .ReturnsAsync(new[] { new ProviderUsage 
            { 
                ProviderId = providerId,
                ProviderName = "GitHub Copilot",
                AccountName = "test@example.com",
                Description = "Usage for test@example.com is 50",
                IsAvailable = true,
                PaymentType = PaymentType.Quota,
                IsQuotaBased = true
            }});

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        await mainWindow.RefreshData(forceRefresh: true);
        
        // Act: Toggle Privacy Mode ON
        var privacyBtn = (System.Windows.Controls.Primitives.ToggleButton)mainWindow.FindName("PrivacyBtn");
        var method = typeof(MainWindow).GetMethod("PrivacyBtn_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(mainWindow, new object[] { privacyBtn, new RoutedEventArgs() });

        // Assert
        var providersList = (StackPanel)mainWindow.FindName("ProvidersList");
        
        bool foundMaskedAccount = false;
        bool foundMaskedDescription = false;
        var sb = new System.Text.StringBuilder();

        foreach (var child in providersList.Children)
        {
            if (child is Border border && border.Child is Grid grid)
            {
                foreach (var gridChild in grid.Children)
                {
                    if (gridChild is Grid headerGrid && Grid.GetRow(headerGrid) == 0)
                    {
                        foreach (var headerChild in headerGrid.Children)
                        {
                            if (headerChild is TextBlock tb)
                            {
                                sb.AppendLine($"Standard Header: '{tb.Text}'");
                                if (tb.Text.Contains("[t**t@example.com]")) foundMaskedAccount = true;
                            }
                        }
                    }
                    
                    if (gridChild is Grid detailGrid && Grid.GetRow(detailGrid) == 1)
                    {
                        foreach (var detailChild in detailGrid.Children)
                        {
                            if (detailChild is TextBlock tb)
                            {
                                sb.AppendLine($"Standard Detail: '{tb.Text}'");
                                // "Usage for test@example.com is 50" -> masks email part
                                if (tb.Text.Contains("Usage for t**t@example.com")) foundMaskedDescription = true;
                            }
                        }
                    }
                }
            }
        }

        Assert.True(foundMaskedAccount, $"Account name was not masked correctly in Standard Mode. Found items:\n{sb.ToString()}");
        Assert.True(foundMaskedDescription, $"Description was not masked correctly in Standard Mode. Found items:\n{sb.ToString()}");
    }

    [WpfFact]
    public async Task PrivacyMode_ShouldMaskPlainUsername_CompactMode()
    {
        // Arrange
        var providerId = "github-copilot";
        _mockConfigLoader.Setup(c => c.LoadPreferencesAsync()).ReturnsAsync(new AppPreferences { ShowAll = true, CompactMode = true });
        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(new List<ProviderConfig> { 
            new ProviderConfig { ProviderId = providerId, ApiKey = "fake-key" }
        });

        _mockProvider.Setup(p => p.ProviderId).Returns(providerId);
        _mockProvider.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>()))
            .ReturnsAsync(new[] { new ProviderUsage 
            { 
                ProviderId = providerId,
                ProviderName = "GitHub Copilot",
                AccountName = "johndoe",
                Description = "Logged in as johndoe",
                IsAvailable = true,
                PaymentType = PaymentType.Quota,
                IsQuotaBased = true
            }});

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        await mainWindow.RefreshData(forceRefresh: true);
        
        // Act: Toggle Privacy Mode ON
        var privacyBtn = (System.Windows.Controls.Primitives.ToggleButton)mainWindow.FindName("PrivacyBtn");
        var method = typeof(MainWindow).GetMethod("PrivacyBtn_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(mainWindow, new object[] { privacyBtn, new RoutedEventArgs() });

        // Assert
        var providersList = (StackPanel)mainWindow.FindName("ProvidersList");
        
        bool foundMaskedUsername = false;
        bool foundMaskedDescription = false;
        var sb = new System.Text.StringBuilder();

        foreach (var child in providersList.Children)
        {
            if (child is Grid grid) 
            {
                foreach (var gridChild in grid.Children)
                {
                    if (gridChild is DockPanel dp)
                    {
                        foreach (var dpChild in dp.Children)
                        {
                            if (dpChild is TextBlock tb)
                            {
                                sb.AppendLine($"Compact Text: '{tb.Text}'");
                                if (tb.Text.Contains("[j*****e]")) foundMaskedUsername = true;
                                if (tb.Text.Contains("Logged in as j*****e")) foundMaskedDescription = true;
                            }
                        }
                    }
                }
            }
        }

        Assert.True(foundMaskedUsername, $"Username was not masked correctly in Compact Mode. Found items:\n{sb.ToString()}");
        Assert.True(foundMaskedDescription, $"Description was not masked correctly in Compact Mode. Found items:\n{sb.ToString()}");
    }
}
