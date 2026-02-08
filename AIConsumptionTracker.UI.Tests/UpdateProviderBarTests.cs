using System.Windows;
using System.Windows.Controls;
using AIConsumptionTracker.UI;
using AIConsumptionTracker.Core.Services;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AIConsumptionTracker.UI.Tests;

[Collection("UI Tests")]
public class UpdateProviderBarTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Mock<IConfigLoader> _mockConfigLoader;
    private readonly Mock<IProviderService> _mockProvider;

    public UpdateProviderBarTests()
    {
        var services = new ServiceCollection();
        
        _mockConfigLoader = new Mock<IConfigLoader>();
        _mockConfigLoader.Setup(c => c.LoadPreferencesAsync()).ReturnsAsync(new AppPreferences { ShowAll = true });
        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(new List<ProviderConfig>());

        _mockProvider = new Mock<IProviderService>();
        _mockProvider.Setup(p => p.ProviderId).Returns("test-provider");
        _mockProvider.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
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
        
        var mockUpdateChecker = new Mock<IUpdateCheckerService>();
        mockUpdateChecker.Setup(u => u.CheckForUpdatesAsync()).ReturnsAsync((UpdateInfo?)null);
        services.AddSingleton(mockUpdateChecker.Object);
        services.AddTransient<MainWindow>();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    [WpfFact]
    public async Task UpdateProviderBar_ShouldNotAddUnavailableProvider_WhenShowAllIsFalse()
    {
        // Arrange
        var providerId = "github-copilot";
        _mockConfigLoader.Setup(c => c.LoadPreferencesAsync()).ReturnsAsync(new AppPreferences { ShowAll = false, CompactMode = true });
        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(new List<ProviderConfig> { 
            new ProviderConfig { ProviderId = providerId, ApiKey = "" }
        });

        _mockProvider.Setup(p => p.ProviderId).Returns(providerId);
        _mockProvider.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
            .ReturnsAsync(new[] { new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "GitHub Copilot",
                AccountName = "",
                Description = "API Key not found",
                IsAvailable = false,
                PaymentType = PaymentType.UsageBased
            }});

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        await mainWindow.RefreshData(forceRefresh: false);

        // Act: Call UpdateProviderBar with an unavailable provider
        var unavailableUsage = new ProviderUsage
        {
            ProviderId = "openai",
            ProviderName = "OpenAI",
            AccountName = "",
            Description = "API Key not found",
            IsAvailable = false,
            PaymentType = PaymentType.UsageBased
        };

        var method = typeof(MainWindow).GetMethod("UpdateProviderBar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(mainWindow, new object[] { unavailableUsage });

        // Assert: The unavailable provider should NOT be added to the UI
        var providersList = (StackPanel)mainWindow.FindName("ProvidersList");
        var providerCount = providersList.Children.OfType<Grid>().Count(g => g.Tag?.ToString() == "openai");
        
        Assert.Equal(0, providerCount);
    }

    [WpfFact]
    public async Task UpdateProviderBar_ShouldAddUnavailableProvider_WhenShowAllIsTrue()
    {
        // Arrange
        var providerId = "github-copilot";
        _mockConfigLoader.Setup(c => c.LoadPreferencesAsync()).ReturnsAsync(new AppPreferences { ShowAll = true, CompactMode = true });
        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(new List<ProviderConfig> { 
            new ProviderConfig { ProviderId = providerId, ApiKey = "" }
        });

        _mockProvider.Setup(p => p.ProviderId).Returns(providerId);
        _mockProvider.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
            .ReturnsAsync(new[] { new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "GitHub Copilot",
                AccountName = "",
                Description = "API Key not found",
                IsAvailable = false,
                PaymentType = PaymentType.UsageBased
            }});

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        await mainWindow.RefreshData(forceRefresh: false);

        // Act: Call UpdateProviderBar with an unavailable provider
        var unavailableUsage = new ProviderUsage
        {
            ProviderId = "openai",
            ProviderName = "OpenAI",
            AccountName = "",
            Description = "API Key not found",
            IsAvailable = false,
            PaymentType = PaymentType.UsageBased
        };

        var method = typeof(MainWindow).GetMethod("UpdateProviderBar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(mainWindow, new object[] { unavailableUsage });

        // Assert: The unavailable provider SHOULD be added to the UI when ShowAll is true
        var providersList = (StackPanel)mainWindow.FindName("ProvidersList");
        var providerCount = providersList.Children.OfType<Grid>().Count(g => g.Tag?.ToString() == "openai");
        
        Assert.Equal(1, providerCount);
    }

    [WpfFact]
    public async Task UpdateProviderBar_ShouldAddQuotaBasedProvider_WhenShowAllIsFalse()
    {
        // Arrange
        var providerId = "github-copilot";
        _mockConfigLoader.Setup(c => c.LoadPreferencesAsync()).ReturnsAsync(new AppPreferences { ShowAll = false, CompactMode = true });
        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(new List<ProviderConfig> { 
            new ProviderConfig { ProviderId = providerId, ApiKey = "" }
        });

        _mockProvider.Setup(p => p.ProviderId).Returns(providerId);
        _mockProvider.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
            .ReturnsAsync(new[] { new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "GitHub Copilot",
                AccountName = "",
                Description = "API Key not found",
                IsAvailable = false,
                PaymentType = PaymentType.UsageBased
            }});

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        await mainWindow.RefreshData(forceRefresh: false);

        // Act: Call UpdateProviderBar with a quota-based provider
        var quotaUsage = new ProviderUsage
        {
            ProviderId = "anthropic",
            ProviderName = "Anthropic",
            AccountName = "",
            Description = "Connected",
            IsAvailable = true,
            PaymentType = PaymentType.Quota,
            IsQuotaBased = true
        };

        var method = typeof(MainWindow).GetMethod("UpdateProviderBar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(mainWindow, new object[] { quotaUsage });

        // Assert: The quota-based provider SHOULD be added even when ShowAll is false
        var providersList = (StackPanel)mainWindow.FindName("ProvidersList");
        var providerCount = providersList.Children.OfType<Grid>().Count(g => g.Tag?.ToString() == "anthropic");
        
        Assert.Equal(1, providerCount);
    }

    [WpfFact]
    public async Task UpdateProviderBar_ShouldAddProviderWithNextResetTime_WhenShowAllIsFalse()
    {
        // Arrange
        var providerId = "github-copilot";
        _mockConfigLoader.Setup(c => c.LoadPreferencesAsync()).ReturnsAsync(new AppPreferences { ShowAll = false, CompactMode = true });
        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(new List<ProviderConfig> { 
            new ProviderConfig { ProviderId = providerId, ApiKey = "" }
        });

        _mockProvider.Setup(p => p.ProviderId).Returns(providerId);
        _mockProvider.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
            .ReturnsAsync(new[] { new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "GitHub Copilot",
                AccountName = "",
                Description = "API Key not found",
                IsAvailable = false,
                PaymentType = PaymentType.UsageBased
            }});

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        await mainWindow.RefreshData(forceRefresh: false);

        // Act: Call UpdateProviderBar with a provider that has a reset time
        var resetTimeUsage = new ProviderUsage
        {
            ProviderId = "openai",
            ProviderName = "OpenAI",
            AccountName = "",
            Description = "Connected",
            IsAvailable = true,
            PaymentType = PaymentType.UsageBased,
            NextResetTime = DateTime.UtcNow.AddDays(7)
        };

        var method = typeof(MainWindow).GetMethod("UpdateProviderBar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(mainWindow, new object[] { resetTimeUsage });

        // Assert: The provider with reset time SHOULD be added even when ShowAll is false
        var providersList = (StackPanel)mainWindow.FindName("ProvidersList");
        var providerCount = providersList.Children.OfType<Grid>().Count(g => g.Tag?.ToString() == "openai");
        
        Assert.Equal(1, providerCount);
    }

    [WpfFact]
    public async Task UpdateProviderBar_ShouldReplaceExistingProviderBar()
    {
        // Arrange
        var providerId = "github-copilot";
        _mockConfigLoader.Setup(c => c.LoadPreferencesAsync()).ReturnsAsync(new AppPreferences { ShowAll = true, CompactMode = true });
        _mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(new List<ProviderConfig> { 
            new ProviderConfig { ProviderId = providerId, ApiKey = "fake-key" }
        });

        _mockProvider.Setup(p => p.ProviderId).Returns(providerId);
        _mockProvider.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
            .ReturnsAsync(new[] { new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "GitHub Copilot",
                AccountName = "",
                Description = "Usage: 50%",
                IsAvailable = true,
                PaymentType = PaymentType.Quota,
                IsQuotaBased = true
            }});

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        await mainWindow.RefreshData(forceRefresh: false);

        // Verify initial state
        var providersList = (StackPanel)mainWindow.FindName("ProvidersList");
        var initialCount = providersList.Children.OfType<Grid>().Count(g => g.Tag?.ToString() == providerId);
        Assert.Equal(1, initialCount);

        // Act: Call UpdateProviderBar with updated data
        var updatedUsage = new ProviderUsage
        {
            ProviderId = providerId,
            ProviderName = "GitHub Copilot",
            AccountName = "",
            Description = "Usage: 75%",
            IsAvailable = true,
            PaymentType = PaymentType.Quota,
            IsQuotaBased = true
        };

        var method = typeof(MainWindow).GetMethod("UpdateProviderBar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(mainWindow, new object[] { updatedUsage });

        // Assert: The old bar should be replaced, not added as a duplicate
        var finalCount = providersList.Children.OfType<Grid>().Count(g => g.Tag?.ToString() == providerId);
        Assert.Equal(1, finalCount);
    }
}
