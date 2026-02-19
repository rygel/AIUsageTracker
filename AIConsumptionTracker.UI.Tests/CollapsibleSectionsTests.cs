using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIConsumptionTracker.UI;
using AIConsumptionTracker.Core.Services;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AIConsumptionTracker.UI.Tests;

[Collection("UI Tests")]
public class CollapsibleSectionsTests
{
    private Mock<IConfigLoader> CreateMockConfigLoader(AppPreferences? preferences = null)
    {
        var mockConfigLoader = new Mock<IConfigLoader>();
        mockConfigLoader.Setup(c => c.LoadPreferencesAsync()).ReturnsAsync(preferences ?? new AppPreferences { ShowAll = true });
        mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(new List<ProviderConfig>());
        return mockConfigLoader;
    }

    private IServiceProvider CreateServiceProvider(
        Mock<IConfigLoader> mockConfigLoader, 
        IProviderService providerService,
        AppPreferences? preferences = null)
    {
        var services = new ServiceCollection();
        
        if (preferences != null)
        {
            mockConfigLoader.Setup(c => c.LoadPreferencesAsync()).ReturnsAsync(preferences);
        }

        var providers = new List<IProviderService> { providerService };
        var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<ProviderManager>>();
        var providerManager = new ProviderManager(providers, mockConfigLoader.Object, mockLogger.Object);
        
        var mockFontProvider = new Mock<IFontProvider>();
        var mockGithubAuth = new Mock<IGitHubAuthService>();
        
        services.AddSingleton(mockConfigLoader.Object);
        services.AddSingleton(providerManager);
        services.AddSingleton(mockFontProvider.Object);
        services.AddSingleton(mockGithubAuth.Object);
        
        var mockUpdateChecker = new Mock<IUpdateCheckerService>();
        mockUpdateChecker.Setup(u => u.CheckForUpdatesAsync()).ReturnsAsync((UpdateInfo?)null);
        services.AddSingleton(mockUpdateChecker.Object);
        
        var mockNotificationService = new Mock<INotificationService>();
        services.AddSingleton(mockNotificationService.Object);
        
        services.AddTransient<MainWindow>();
        
        return services.BuildServiceProvider();
    }

    [WpfFact]
    public async Task CollapsibleSections_ShouldRenderWithToggleIcons()
    {
        // Arrange
        var providerId = "synthetic";
        var mockConfigLoader = CreateMockConfigLoader();
        mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(new List<ProviderConfig> { 
            new ProviderConfig { ProviderId = providerId, ApiKey = "fake-key" }
        });

        var provider = new MockProviderService
        {
            ProviderId = providerId,
            UsageHandler = config => Task.FromResult<IEnumerable<ProviderUsage>>(new[] { new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "Synthetic",
                Description = "Test",
                IsAvailable = true,
                PlanType = PlanType.Coding
            }})
        };

        var serviceProvider = CreateServiceProvider(mockConfigLoader, provider);
        var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        await mainWindow.RefreshData(forceRefresh: false);

        // Assert
        var providersList = (StackPanel)mainWindow.FindName("ProvidersList");
        
        // Find collapsible headers (should have ▼ or ▶)
        var headers = FindAllTextBlocksRecursive(providersList)
            .Where(tb => tb.Text == "▼" || tb.Text == "▶")
            .ToList();
        
        Assert.True(headers.Count >= 1, "Should have at least one collapsible section header with toggle icon");
    }

    [WpfFact]
    public async Task CollapsibleSection_StateShouldPersist()
    {
        // Arrange
        var preferences = new AppPreferences 
        { 
            ShowAll = true,
            IsPlansAndQuotasCollapsed = true  // Start collapsed
        };
        var mockConfigLoader = CreateMockConfigLoader(preferences);

        var providerId = "synthetic";
        mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(new List<ProviderConfig> { 
            new ProviderConfig { ProviderId = providerId, ApiKey = "fake-key" }
        });

        var provider = new MockProviderService
        {
            ProviderId = providerId,
            UsageHandler = config => Task.FromResult<IEnumerable<ProviderUsage>>(new[] { new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "Synthetic",
                Description = "Test",
                IsAvailable = true,
                PlanType = PlanType.Coding
            }})
        };

        var serviceProvider = CreateServiceProvider(mockConfigLoader, provider, preferences);
        var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        await mainWindow.RefreshData(forceRefresh: false);

        // Assert - when IsPlansAndQuotasCollapsed is true, the toggle should show ▶
        var providersList = (StackPanel)mainWindow.FindName("ProvidersList");
        var toggleIcon = FindAllTextBlocksRecursive(providersList)
            .FirstOrDefault(tb => tb.Text == "▶" && tb.Tag?.ToString() == "ToggleIcon");
        
        Assert.NotNull(toggleIcon);
    }

    [WpfFact]
    public async Task Antigravity_ShouldHaveSubProviderCollapsibleSection()
    {
        // Arrange
        var providerId = "antigravity";
        var mockConfigLoader = CreateMockConfigLoader();
        mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(new List<ProviderConfig> { 
            new ProviderConfig { ProviderId = providerId, ApiKey = "fake-key" }
        });

        var provider = new MockProviderService
        {
            ProviderId = providerId,
            UsageHandler = config => Task.FromResult<IEnumerable<ProviderUsage>>(new[] { new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "Antigravity",
                Description = "Test",
                IsAvailable = true,
                PlanType = PlanType.Coding,
                Details = new List<ProviderUsageDetail>
                {
                    new ProviderUsageDetail { Name = "Sub-provider 1", Used = "50%" },
                    new ProviderUsageDetail { Name = "Sub-provider 2", Used = "75%" }
                }
            }})
        };

        var serviceProvider = CreateServiceProvider(mockConfigLoader, provider);
        var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        await mainWindow.RefreshData(forceRefresh: false);

        // Assert
        var providersList = (StackPanel)mainWindow.FindName("ProvidersList");
        
        // Look for "Sub-providers" header text
        var subProviderHeader = FindAllTextBlocksRecursive(providersList)
            .FirstOrDefault(tb => tb.Text == "Sub-providers");
        
        Assert.NotNull(subProviderHeader);
    }

    [WpfFact]
    public async Task CollapsibleSection_Click_ShouldToggleVisibility()
    {
        // Arrange
        var providerId = "synthetic";
        var mockConfigLoader = CreateMockConfigLoader();
        mockConfigLoader.Setup(c => c.LoadConfigAsync()).ReturnsAsync(new List<ProviderConfig> { 
            new ProviderConfig { ProviderId = providerId, ApiKey = "fake-key" }
        });

        var provider = new MockProviderService
        {
            ProviderId = providerId,
            UsageHandler = config => Task.FromResult<IEnumerable<ProviderUsage>>(new[] { new ProviderUsage
            {
                ProviderId = providerId,
                ProviderName = "Synthetic",
                Description = "Test",
                IsAvailable = true,
                PlanType = PlanType.Coding
            }})
        };

        var serviceProvider = CreateServiceProvider(mockConfigLoader, provider);
        var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        await mainWindow.RefreshData(forceRefresh: false);

        // Act - Find and click the header
        var providersList = (StackPanel)mainWindow.FindName("ProvidersList");
        var headerGrid = FindAllGridsRecursive(providersList)
            .FirstOrDefault(g => g.Children.OfType<TextBlock>().Any(tb => tb.Text == "▼"));
        
        Assert.NotNull(headerGrid);

        // Simulate click
        var mouseEvent = new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left);
        mouseEvent.RoutedEvent = UIElement.MouseLeftButtonDownEvent;
        headerGrid.RaiseEvent(mouseEvent);

        // Assert - After click, the icon should change to ▶
        var toggleIcon = FindAllTextBlocksRecursive(providersList)
            .FirstOrDefault(tb => tb.Text == "▶" && tb.Tag?.ToString() == "ToggleIcon");
        
        Assert.NotNull(toggleIcon);
    }

    // Helper methods
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

    private static IEnumerable<Grid> FindAllGridsRecursive(DependencyObject parent)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Grid grid)
            {
                yield return grid;
            }
            
            foreach (var result in FindAllGridsRecursive(child))
            {
                yield return result;
            }
        }
    }
}
