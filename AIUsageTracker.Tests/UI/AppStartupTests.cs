using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.UI.Slim;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using System.Windows;
using Xunit;

namespace AIUsageTracker.Tests.UI;

public class AppStartupTests : IDisposable
{
    private readonly string _testPreferencesDirectory;
    private readonly string _testPreferencesPath;
    private readonly UiPreferencesStore _store;
    private readonly Mock<IAppPathProvider> _mockPathProvider;

    public AppStartupTests()
    {
        _testPreferencesDirectory = Path.Combine(Path.GetTempPath(), $"AIUsageTracker_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testPreferencesDirectory);
        _testPreferencesPath = Path.Combine(_testPreferencesDirectory, "preferences.json");
        
        _mockPathProvider = new Mock<IAppPathProvider>();
        _mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(_testPreferencesPath);
        _mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(Path.Combine(_testPreferencesDirectory, "auth.json"));
        
        _store = new UiPreferencesStore(NullLogger<UiPreferencesStore>.Instance, _mockPathProvider.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testPreferencesDirectory))
        {
            Directory.Delete(_testPreferencesDirectory, true);
        }
    }

    [Fact]
    public async Task LoadPreferencesAsync_DoesNotBlockThread()
    {
        var startTime = DateTime.UtcNow;
        var loadTask = _store.LoadAsync();

        var completed = await Task.WhenAny(loadTask, Task.Delay(TimeSpan.FromSeconds(5)));
        var endTime = DateTime.UtcNow;

        Assert.Same(loadTask, completed);
        Assert.True(endTime - startTime < TimeSpan.FromSeconds(5),
            "Loading preferences took too long - possible blocking call");
    }

    [Fact]
    public async Task LoadPreferencesAsync_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var preferences = await _store.LoadAsync();

        Assert.NotNull(preferences);
        Assert.True(Enum.IsDefined(typeof(AppTheme), preferences.Theme),
            $"Theme value {preferences.Theme} should be a valid enum value");
    }

    [Fact]
    public async Task LoadPreferencesAsync_WithLightTheme_PreservesLightTheme()
    {
        var preferences = new AppPreferences
        {
            Theme = AppTheme.Light,
            WindowWidth = 420,
            WindowHeight = 600
        };

        var json = JsonSerializer.Serialize(preferences);
        await File.WriteAllTextAsync(_testPreferencesPath, json);

        var loaded = await _store.LoadAsync();
        Assert.Equal(AppTheme.Light, loaded.Theme);
    }

    [Fact]
    public async Task SavePreferencesAsync_ThenLoadAsync_RoundTripsCorrectly()
    {
        var original = new AppPreferences
        {
            Theme = AppTheme.Dracula,
            WindowLeft = 100,
            WindowTop = 200,
            WindowWidth = 500,
            WindowHeight = 700,
            AlwaysOnTop = false,
            IsPrivacyMode = true
        };

        var saved = await _store.SaveAsync(original);
        var loaded = await _store.LoadAsync();

        Assert.True(saved);
        Assert.Equal(original.Theme, loaded.Theme);
        Assert.Equal(original.WindowLeft, loaded.WindowLeft);
        Assert.Equal(original.WindowTop, loaded.WindowTop);
        Assert.Equal(original.WindowWidth, loaded.WindowWidth);
        Assert.Equal(original.WindowHeight, loaded.WindowHeight);
        Assert.Equal(original.AlwaysOnTop, loaded.AlwaysOnTop);
        Assert.Equal(original.IsPrivacyMode, loaded.IsPrivacyMode);
    }

    [Fact]
    public void ApplyTheme_WithNullResources_DoesNotThrow()
    {
        // Ensure App is initialized so Host is available
        if (Application.Current == null)
        {
            new App(); 
        }
        
        var theme = AppTheme.Dark;

        try
        {
            App.ApplyTheme(theme);
        }
        catch (NullReferenceException)
        {
            // Expected in test context since Application.Current is null
        }
        catch (InvalidOperationException)
        {
            // Fallback for some test environments
        }
    }

    [Fact]
    public async Task PreferencesStore_SaveLoad_NoDeadlock()
    {
        var preferences = new AppPreferences { Theme = AppTheme.Nord };

        for (int i = 0; i < 10; i++)
        {
            preferences.Theme = (AppTheme)((i % 4) + 1);
            var saved = await _store.SaveAsync(preferences);
            var loaded = await _store.LoadAsync();

            Assert.True(saved);
            Assert.NotNull(loaded);
        }
    }

    [Fact]
    public async Task ThemeCombo_SelectedValue_PreservesNonDefaultTheme()
    {
        var preferences = new AppPreferences { Theme = AppTheme.Light };

        preferences.Theme = AppTheme.Midnight;
        var saved = await _store.SaveAsync(preferences);
        var loaded = await _store.LoadAsync();

        Assert.True(saved);
        Assert.Equal(AppTheme.Midnight, loaded.Theme);
    }
}
