using System.Windows;
using AIConsumptionTracker.UI;
using AIConsumptionTracker.Core.Services;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AIConsumptionTracker.UI.Tests;

public class HeadlessWpfTests
{
    private readonly IServiceProvider _serviceProvider;

    public HeadlessWpfTests()
    {
        var services = new ServiceCollection();
        
        // Mock dependencies
        var mockConfigLoader = new Mock<IConfigLoader>();
        var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<ProviderManager>>();
        var providers = new List<IProviderService>();
        
        // Use real ProviderManager but with mocked dependencies
        var providerManager = new ProviderManager(providers, mockConfigLoader.Object, mockLogger.Object);
        
        services.AddSingleton(mockConfigLoader.Object);
        services.AddSingleton(providerManager);
        services.AddTransient<SettingsWindow>();
        services.AddTransient<MainWindow>();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    [WpfFact]
    public void ClosingSettingsWithoutSaving_ShouldNotSetSettingsChanged()
    {
        var settingsWindow = _serviceProvider.GetRequiredService<SettingsWindow>();
        
        // SettingsChanged should be false by default
        Assert.False(settingsWindow.SettingsChanged);
        
        // Simulate clicking 'Save' (which we aren't doing here, we are testing the default/cancel case)
        // If we close it, it should still be false
        settingsWindow.Close();
        
        Assert.False(settingsWindow.SettingsChanged);
    }

    [WpfFact]
    public void SavingSettings_ShouldSetSettingsChanged()
    {
        var settingsWindow = _serviceProvider.GetRequiredService<SettingsWindow>();
        
        // Since we can't easily click a button in a unit test without showing it (UI automation style),
        // we can test the internal state if we have access, or just verify the SaveBtn_Click logic.
        
        // Let's use reflection to call SaveBtn_Click for a "white-box" headless test 
        // since we are testing internal logic in a headless way.
        var method = typeof(SettingsWindow).GetMethod("SaveBtn_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(settingsWindow, new object[] { null!, null! });
        
        Assert.True(settingsWindow.SettingsChanged);
    }
}
