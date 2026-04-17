// <copyright file="AppStartupTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Tests.Infrastructure;
using AIUsageTracker.UI.Slim;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
        this._testPreferencesDirectory = TestTempPaths.CreateDirectory("AIUsageTracker-Test");
        this._testPreferencesPath = Path.Combine(this._testPreferencesDirectory, "preferences.json");

        this._mockPathProvider = new Mock<IAppPathProvider>();
        this._mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(this._testPreferencesPath);
        this._mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(Path.Combine(this._testPreferencesDirectory, "auth.json"));
        this._mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this._testPreferencesDirectory);

        this._store = new UiPreferencesStore(NullLogger<UiPreferencesStore>.Instance, this._mockPathProvider.Object);
    }

    public void Dispose()
    {
        TestTempPaths.CleanupPath(this._testPreferencesDirectory);
    }

    [Fact]
    public async Task LoadPreferencesAsync_DoesNotBlockThreadAsync()
    {
        var startTime = DateTime.UtcNow;
        var loadTask = this._store.LoadAsync();

        var completed = await Task.WhenAny(loadTask, Task.Delay(TimeSpan.FromSeconds(5)));
        var endTime = DateTime.UtcNow;

        Assert.Same(loadTask, completed);
        Assert.True(
            endTime - startTime < TimeSpan.FromSeconds(5),
            "Loading preferences took too long - possible blocking call");
    }

    [Fact]
    public async Task LoadPreferencesAsync_WhenFileDoesNotExist_ReturnsDefaultsAsync()
    {
        var preferences = await this._store.LoadAsync();

        Assert.NotNull(preferences);
        Assert.True(
            Enum.IsDefined(typeof(AppTheme), preferences.Theme),
            $"Theme value {preferences.Theme} should be a valid enum value");
    }

    [Fact]
    public async Task LoadPreferencesAsync_WithLightTheme_PreservesLightThemeAsync()
    {
        var preferences = new AppPreferences
        {
            Theme = AppTheme.Light,
            WindowWidth = 420,
            WindowHeight = 600,
        };

        var json = JsonSerializer.Serialize(preferences);
        await File.WriteAllTextAsync(this._testPreferencesPath, json);

        var loaded = await this._store.LoadAsync();
        Assert.Equal(AppTheme.Light, loaded.Theme);
    }

    [Fact]
    public async Task SavePreferencesAsync_ThenLoadAsync_RoundTripsCorrectlyAsync()
    {
        var original = new AppPreferences
        {
            Theme = AppTheme.Dracula,
            WindowLeft = 100,
            WindowTop = 200,
            WindowWidth = 500,
            WindowHeight = 700,
            AlwaysOnTop = false,
            IsPrivacyMode = true,
            ShowUsedPercentages = true,
        };

        var saved = await this._store.SaveAsync(original);
        var loaded = await this._store.LoadAsync();

        Assert.True(saved);
        Assert.Equal(original.Theme, loaded.Theme);
        Assert.Equal(original.WindowLeft, loaded.WindowLeft);
        Assert.Equal(original.WindowTop, loaded.WindowTop);
        Assert.Equal(original.WindowWidth, loaded.WindowWidth);
        Assert.Equal(original.WindowHeight, loaded.WindowHeight);
        Assert.Equal(original.AlwaysOnTop, loaded.AlwaysOnTop);
        Assert.Equal(original.IsPrivacyMode, loaded.IsPrivacyMode);
        Assert.Equal(original.PercentageDisplayMode, loaded.PercentageDisplayMode);
        Assert.True(loaded.ShowUsedPercentages);
    }

    [Fact]
    public async Task LoadPreferencesAsync_WithLegacyInvertCalculations_MapsToShowUsedPercentagesAsync()
    {
        await File.WriteAllTextAsync(this._testPreferencesPath, "{\"InvertCalculations\":true}");

        var loaded = await this._store.LoadAsync();

        Assert.True(loaded.ShowUsedPercentages);
        Assert.Equal(PercentageDisplayMode.Used, loaded.PercentageDisplayMode);
    }

    [Fact]
    public void ApplyTheme_WithNullResources_DoesNotThrow()
    {
        var theme = AppTheme.Dark;

        var exception = Record.Exception(() => App.ApplyTheme(theme));

        Assert.Null(exception);
    }

    [Fact]
    public async Task PreferencesStore_SaveLoad_NoDeadlockAsync()
    {
        var preferences = new AppPreferences { Theme = AppTheme.Nord };

        for (int i = 0; i < 10; i++)
        {
            preferences.Theme = (AppTheme)((i % 4) + 1);
            var saved = await this._store.SaveAsync(preferences);
            var loaded = await this._store.LoadAsync();

            Assert.True(saved);
            Assert.NotNull(loaded);
        }
    }

    [Fact]
    public async Task ThemeCombo_SelectedValue_PreservesNonDefaultThemeAsync()
    {
        var preferences = new AppPreferences { Theme = AppTheme.Light };

        preferences.Theme = AppTheme.Midnight;
        var saved = await this._store.SaveAsync(preferences);
        var loaded = await this._store.LoadAsync();

        Assert.True(saved);
        Assert.Equal(AppTheme.Midnight, loaded.Theme);
    }

    [Fact]
    public async Task SavePreferencesAsync_ThenLoadAsync_RoundTripsUpdateChannelAsync()
    {
        var preferences = new AppPreferences { UpdateChannel = UpdateChannel.Beta };

        var saved = await this._store.SaveAsync(preferences);
        var loaded = await this._store.LoadAsync();

        Assert.True(saved);
        Assert.Equal(UpdateChannel.Beta, loaded.UpdateChannel);
    }

    [Fact]
    public async Task LoadPreferencesAsync_WhenPrimaryCorrupted_UsesBackupAsync()
    {
        var original = new AppPreferences
        {
            Theme = AppTheme.Nord,
            IsPrivacyMode = true,
        };
        var updated = new AppPreferences
        {
            Theme = AppTheme.Dracula,
            IsPrivacyMode = false,
        };

        // First save creates primary, second save creates .bak from first state.
        Assert.True(await this._store.SaveAsync(original));
        Assert.True(await this._store.SaveAsync(updated));

        await File.WriteAllTextAsync(this._testPreferencesPath, "{ invalid json");
        var loaded = await this._store.LoadAsync();

        Assert.Equal(AppTheme.Nord, loaded.Theme);
        Assert.True(loaded.IsPrivacyMode);
    }

    [Fact]
    public async Task SavePreferencesAsync_ConcurrentWrites_RemainReadableAsync()
    {
        var saveTasks = Enumerable.Range(0, 40)
            .Select(async index =>
            {
                var prefs = new AppPreferences
                {
                    Theme = (AppTheme)(index % 6),
                    IsPrivacyMode = index % 2 == 0,
                    WindowLeft = 10 + index,
                    WindowTop = 20 + index,
                };
                return await this._store.SaveAsync(prefs);
            });

#pragma warning disable MA0004 // xUnit test methods should avoid ConfigureAwait(false) (xUnit1030).
        var results = await Task.WhenAll(saveTasks);
#pragma warning restore MA0004
        Assert.All(results, Assert.True);

        var loaded = await this._store.LoadAsync();
        Assert.True(Enum.IsDefined(typeof(AppTheme), loaded.Theme));
    }
}
