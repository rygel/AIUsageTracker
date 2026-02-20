using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIConsumptionTracker.UI;
using AIConsumptionTracker.Core.Services;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Infrastructure.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AIConsumptionTracker.UI.Tests;

[Collection("UI Tests")]
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
        _mockProvider.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
            .ReturnsAsync(new[] { new ProviderUsage
            {
                ProviderId = "test-provider",
                ProviderName = "Test Provider",
                AccountName = "test@example.com",
                Description = "Usage for test@example.com is 50",
                IsAvailable = true,
                PlanType = PlanType.Coding
            }});
        
        var providers = new List<IProviderService> { _mockProvider.Object };
        var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<ProviderManager>>();
        var providerManager = new ProviderManager(providers, _mockConfigLoader.Object, mockLogger.Object);
        
        var mockFontProvider = new Mock<IFontProvider>();
        var mockGithubAuth = new Mock<IGitHubAuthService>();
        
        services.AddSingleton(_mockConfigLoader.Object);
        services.AddSingleton(providerManager);
        services.AddSingleton(mockFontProvider.Object);
        services.AddSingleton(mockFontProvider.Object);
        services.AddSingleton(mockGithubAuth.Object);
        services.AddSingleton<AIConsumptionTracker.Core.AgentClient.AgentService>();
        
        var mockUpdateChecker = new Mock<IUpdateCheckerService>();
         mockUpdateChecker.Setup(u => u.CheckForUpdatesAsync()).ReturnsAsync((UpdateInfo?)null);
         services.AddSingleton(mockUpdateChecker.Object);
         
         // Add mock notification service
         var mockNotificationService = new Mock<INotificationService>();
         services.AddSingleton(mockNotificationService.Object);
         
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
        _mockProvider.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
            .ReturnsAsync(new[] { new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "GitHub Copilot",
                AccountName = "test@example.com",
                Description = "Usage for test@example.com is 50",
                IsAvailable = true,
                PlanType = PlanType.Coding,
                IsQuotaBased = true
            }});

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        await mainWindow.RefreshData(forceRefresh: true);
        
        // Act: Toggle Privacy Mode ON
        var privacyBtn = (Button)mainWindow.FindName("PrivacyBtn");
        var method = typeof(MainWindow).GetMethod("PrivacyBtn_ClickAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var task = (Task)method?.Invoke(mainWindow, new object[] { privacyBtn, new RoutedEventArgs() })!;
        await task;

        // Assert
        var providersList = (StackPanel)mainWindow.FindName("ProvidersList");
        
        bool foundMaskedAccount = false;
        bool foundMaskedDescription = false;
        var expectedMasked = PrivacyHelper.MaskContent("test@example.com", "test@example.com");
        var sb = new System.Text.StringBuilder();

        // Search recursively through all UI elements
        foreach (var textBlock in FindAllTextBlocksRecursive(providersList))
        {
            sb.AppendLine($"Text: '{textBlock.Text}'");
            if (textBlock.Text.Contains($"[{expectedMasked}]")) foundMaskedAccount = true;
            if (textBlock.Text.Contains($"Usage for {expectedMasked}")) foundMaskedDescription = true;
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
        _mockProvider.Setup(p => p.GetUsageAsync(It.IsAny<ProviderConfig>(), It.IsAny<Action<ProviderUsage>?>()))
            .ReturnsAsync(new[] { new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "GitHub Copilot",
                AccountName = "johndoe",
                Description = "Logged in as johndoe",
                IsAvailable = true,
                PlanType = PlanType.Coding,
                IsQuotaBased = true
            }});

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        await mainWindow.RefreshData(forceRefresh: true);
        
        // Act: Toggle Privacy Mode ON
        var privacyBtn = (Button)mainWindow.FindName("PrivacyBtn");
        var method = typeof(MainWindow).GetMethod("PrivacyBtn_ClickAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var task = (Task)method?.Invoke(mainWindow, new object[] { privacyBtn, new RoutedEventArgs() })!;
        if (task != null) await task;

        // Assert
        var providersList = (StackPanel)mainWindow.FindName("ProvidersList");
        
        bool foundMaskedUsername = false;
        bool foundMaskedDescription = false;
        var sb = new System.Text.StringBuilder();

        // Search recursively through all UI elements
        foreach (var textBlock in FindAllTextBlocksRecursive(providersList))
        {
            sb.AppendLine($"Compact Text: '{textBlock.Text}'");
            if (textBlock.Text.Contains("[j*****e]")) foundMaskedUsername = true;
            if (textBlock.Text.Contains("Logged in as j*****e")) foundMaskedDescription = true;
        }

        Assert.True(foundMaskedUsername, $"Username was not masked correctly in Compact Mode. Found items:\n{sb.ToString()}");
        Assert.True(foundMaskedDescription, $"Description was not masked correctly in Compact Mode. Found items:\n{sb.ToString()}");
    }

    // Helper method to recursively find all TextBlock elements
    private static IEnumerable<TextBlock> FindAllTextBlocksRecursive(DependencyObject parent)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock textBlock)
            {
                yield return textBlock;
            }
            
            foreach (var result in FindAllTextBlocksRecursive(child))
            {
                yield return result;
            }
        }
    }
}
