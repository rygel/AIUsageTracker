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
        Assert.Equal(original.ShowUsedPercentages, loaded.ShowUsedPercentages);
        Assert.True(loaded.ShowUsedPercentages);
    }

    [Fact]
    public async Task LoadPreferencesAsync_WithLegacyPercentageDisplayModeUsed_MapsToShowUsedPercentagesAsync()
    {
        var legacyJson = /*lang=json,strict*/ """
            {
              "PercentageDisplayMode": "Used",
              "SchemaVersion": 2
            }
            """;
        await File.WriteAllTextAsync(this._testPreferencesPath, legacyJson);

        var loaded = await this._store.LoadAsync();

        Assert.True(loaded.ShowUsedPercentages);
    }

    [Fact]
    public async Task LoadPreferencesAsync_WithLegacyPercentageDisplayModeRemaining_MapsToShowUsedPercentagesFalseAsync()
    {
        var legacyJson = /*lang=json,strict*/ """
            {
              "PercentageDisplayMode": "Remaining",
              "SchemaVersion": 2
            }
            """;
        await File.WriteAllTextAsync(this._testPreferencesPath, legacyJson);

        var loaded = await this._store.LoadAsync();

        Assert.False(loaded.ShowUsedPercentages);
    }

    [Fact]
    public async Task LoadPreferencesAsync_WithLegacyPercentageDisplayMode_SavesShowUsedPercentagesOnNextWriteAsync()
    {
        var legacyJson = /*lang=json,strict*/ """
            {
              "PercentageDisplayMode": "Used",
              "SchemaVersion": 2
            }
            """;
        await File.WriteAllTextAsync(this._testPreferencesPath, legacyJson);

        var loaded = await this._store.LoadAsync();
        Assert.True(loaded.ShowUsedPercentages);

        await this._store.SaveAsync(loaded);
        var savedJson = await File.ReadAllTextAsync(this._testPreferencesPath);

        Assert.Contains("ShowUsedPercentages", savedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("PercentageDisplayMode", savedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadPreferencesAsync_WithLegacyInvertCalculations_MapsToShowUsedPercentagesAsync()
    {
        await File.WriteAllTextAsync(this._testPreferencesPath, "{\"InvertCalculations\":true}");

        var loaded = await this._store.LoadAsync();

        Assert.True(loaded.ShowUsedPercentages);
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
    public async Task SavePreferencesAsync_WritesNumericUpdateChannelValueAsync()
    {
        var preferences = new AppPreferences { UpdateChannel = UpdateChannel.Beta };

        var saved = await this._store.SaveAsync(preferences);
        var json = await File.ReadAllTextAsync(this._testPreferencesPath);

        Assert.True(saved);
        Assert.Contains("\"UpdateChannel\": 1", json, StringComparison.Ordinal);
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
                return await this._store.SaveAsync(prefs).ConfigureAwait(false);
            });

#pragma warning disable MA0004 // xUnit test methods should avoid ConfigureAwait(false) (xUnit1030).
        var results = await Task.WhenAll(saveTasks);
#pragma warning restore MA0004
        Assert.All(results, Assert.True);

        var loaded = await this._store.LoadAsync();
        Assert.True(Enum.IsDefined(typeof(AppTheme), loaded.Theme));
    }

    /// <summary>
    /// When the preferences file is unreadable (e.g. file locked during update restart),
    /// LoadAsync must return defaults without throwing. This prevents App.xaml.cs from
    /// entering its catch block and creating a second set of defaults that overwrites
    /// the real settings on the next save.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task LoadAsync_WhenFileCorrupt_ReturnsDefaultsWithoutThrowingAsync()
    {
        await File.WriteAllTextAsync(this._testPreferencesPath, "{ corrupt");

        var loaded = await this._store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(AppTheme.Dark, loaded.Theme);
        Assert.False(loaded.ShowUsedPercentages);
    }

    /// <summary>
    /// Regression: commit d9036e5d made LoadAsync re-throw when the file was locked
    /// and no .bak existed. App.xaml.cs caught the throw, created defaults, and those
    /// defaults overwrote real user settings on the next save. LoadAsync must NEVER throw.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task LoadAsync_WhenFileLocked_NeverThrowsAsync()
    {
        await File.WriteAllTextAsync(
            this._testPreferencesPath,
            JsonSerializer.Serialize(new AppPreferences { Theme = AppTheme.Nord }));

        await using var lockStream = new FileStream(
            this._testPreferencesPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var loaded = await this._store.LoadAsync();

        Assert.NotNull(loaded);
    }

    /// <summary>
    /// LoadAsync must be read-only — it must never modify the existing file.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task LoadAsync_DoesNotModifyExistingFileAsync()
    {
        var originalContent = JsonSerializer.Serialize(new AppPreferences
        {
            Theme = AppTheme.Light,
            ShowUsedPercentages = true,
        });
        await File.WriteAllTextAsync(this._testPreferencesPath, originalContent);

        _ = await this._store.LoadAsync();

        var fileContent = await File.ReadAllTextAsync(this._testPreferencesPath);
        Assert.Equal(originalContent, fileContent);
    }

    /// <summary>
    /// SaveAsync must not create backup (.bak) files — the backup mechanism was
    /// removed because it caused the reset bug (defaults were backed up and restored).
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task SaveAsync_DoesNotCreateBackupFileAsync()
    {
        await this._store.SaveAsync(new AppPreferences { Theme = AppTheme.Light });

        var bakPath = this._testPreferencesPath + ".bak";
        Assert.False(File.Exists(bakPath), ".bak file should not exist after save");
    }

    /// <summary>
    /// Comprehensive round-trip: every user-facing preference must survive
    /// a save-load cycle. This catches serialization regressions.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task SaveAsync_LoadAsync_RoundTripsAllUserPreferencesAsync()
    {
        var original = new AppPreferences
        {
            ShowAll = true,
            WindowWidth = 350,
            WindowHeight = 600,
            WindowLeft = 100,
            WindowTop = 200,
            StayOpen = true,
            AlwaysOnTop = false,
            AggressiveAlwaysOnTop = true,
            ForceWin32Topmost = true,
            CompactMode = false,
            ColorThresholdYellow = 50,
            ColorThresholdRed = 90,
            ShowUsedPercentages = true,
            FontFamily = "Consolas",
            FontSize = 14,
            FontBold = true,
            FontItalic = true,
            AutoRefreshInterval = 120,
            MaxConcurrentProviderRequests = 4,
            IsPrivacyMode = true,
            EnableNotifications = true,
            NotificationThreshold = 85.5,
            NotifyOnUsageThreshold = false,
            NotifyOnQuotaExceeded = false,
            NotifyOnProviderErrors = true,
            NotifyOnSubscriptionExpired = false,
            EnableQuietHours = true,
            QuietHoursStart = "23:00",
            QuietHoursEnd = "06:00",
            StartUiWithWindows = true,
            Theme = AppTheme.SolarizedDark,
            DebugMode = true,
            IsPlansAndQuotasCollapsed = true,
            IsPayAsYouGoCollapsed = true,
            IsAntigravityCollapsed = true,
            HiddenProviderItemIds = ["openai", "gemini"],
            SuppressedProviderIds = ["deepseek", "kimi"],
            CollapsedGroupIds = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["group1"] = true,
                ["group2"] = false,
            },
            UpdateChannel = UpdateChannel.Beta,
            ShowInactiveProviders = true,
            UseRelativeResetTime = true,
            ShowUsagePerHour = true,
            ShowDualQuotaBars = false,
            DualQuotaSingleBarMode = DualQuotaSingleBarMode.Burst,
            EnablePaceAdjustment = false,
            CardPrimaryBadge = CardSlotContent.UsageRate,
            CardSecondaryBadge = CardSlotContent.PaceBadge,
            CardStatusLine = CardSlotContent.ResetAbsolute,
            CardResetInfo = CardSlotContent.StatusText,
            CardCompactMode = true,
            CardBackgroundBar = false,
        };

        await this._store.SaveAsync(original);
        var loaded = await this._store.LoadAsync();

        // Verify every field
        Assert.Equal(original.ShowAll, loaded.ShowAll);
        Assert.Equal(original.WindowWidth, loaded.WindowWidth);
        Assert.Equal(original.WindowHeight, loaded.WindowHeight);
        Assert.Equal(original.WindowLeft, loaded.WindowLeft);
        Assert.Equal(original.WindowTop, loaded.WindowTop);
        Assert.Equal(original.StayOpen, loaded.StayOpen);
        Assert.Equal(original.AlwaysOnTop, loaded.AlwaysOnTop);
        Assert.Equal(original.AggressiveAlwaysOnTop, loaded.AggressiveAlwaysOnTop);
        Assert.Equal(original.ForceWin32Topmost, loaded.ForceWin32Topmost);
        Assert.Equal(original.CompactMode, loaded.CompactMode);
        Assert.Equal(original.ColorThresholdYellow, loaded.ColorThresholdYellow);
        Assert.Equal(original.ColorThresholdRed, loaded.ColorThresholdRed);
        Assert.Equal(original.ShowUsedPercentages, loaded.ShowUsedPercentages);
        Assert.Equal(original.FontFamily, loaded.FontFamily);
        Assert.Equal(original.FontSize, loaded.FontSize);
        Assert.Equal(original.FontBold, loaded.FontBold);
        Assert.Equal(original.FontItalic, loaded.FontItalic);
        Assert.Equal(original.AutoRefreshInterval, loaded.AutoRefreshInterval);
        Assert.Equal(original.MaxConcurrentProviderRequests, loaded.MaxConcurrentProviderRequests);
        Assert.Equal(original.IsPrivacyMode, loaded.IsPrivacyMode);
        Assert.Equal(original.EnableNotifications, loaded.EnableNotifications);
        Assert.Equal(original.NotificationThreshold, loaded.NotificationThreshold);
        Assert.Equal(original.NotifyOnUsageThreshold, loaded.NotifyOnUsageThreshold);
        Assert.Equal(original.NotifyOnQuotaExceeded, loaded.NotifyOnQuotaExceeded);
        Assert.Equal(original.NotifyOnProviderErrors, loaded.NotifyOnProviderErrors);
        Assert.Equal(original.NotifyOnSubscriptionExpired, loaded.NotifyOnSubscriptionExpired);
        Assert.Equal(original.EnableQuietHours, loaded.EnableQuietHours);
        Assert.Equal(original.QuietHoursStart, loaded.QuietHoursStart);
        Assert.Equal(original.QuietHoursEnd, loaded.QuietHoursEnd);
        Assert.Equal(original.StartUiWithWindows, loaded.StartUiWithWindows);
        Assert.Equal(original.Theme, loaded.Theme);
        Assert.Equal(original.DebugMode, loaded.DebugMode);
        Assert.Equal(original.IsPlansAndQuotasCollapsed, loaded.IsPlansAndQuotasCollapsed);
        Assert.Equal(original.IsPayAsYouGoCollapsed, loaded.IsPayAsYouGoCollapsed);
        Assert.Equal(original.IsAntigravityCollapsed, loaded.IsAntigravityCollapsed);
        Assert.Equal(original.HiddenProviderItemIds, loaded.HiddenProviderItemIds);
        Assert.Equal(original.SuppressedProviderIds, loaded.SuppressedProviderIds);
        Assert.Equal(original.CollapsedGroupIds, loaded.CollapsedGroupIds);
        Assert.Equal(original.UpdateChannel, loaded.UpdateChannel);
        Assert.Equal(original.ShowInactiveProviders, loaded.ShowInactiveProviders);
        Assert.Equal(original.UseRelativeResetTime, loaded.UseRelativeResetTime);
        Assert.Equal(original.ShowUsagePerHour, loaded.ShowUsagePerHour);
        Assert.Equal(original.ShowDualQuotaBars, loaded.ShowDualQuotaBars);
        Assert.Equal(original.DualQuotaSingleBarMode, loaded.DualQuotaSingleBarMode);
        Assert.Equal(original.EnablePaceAdjustment, loaded.EnablePaceAdjustment);
        Assert.Equal(original.CardPrimaryBadge, loaded.CardPrimaryBadge);
        Assert.Equal(original.CardSecondaryBadge, loaded.CardSecondaryBadge);
        Assert.Equal(original.CardStatusLine, loaded.CardStatusLine);
        Assert.Equal(original.CardResetInfo, loaded.CardResetInfo);
        Assert.Equal(original.CardCompactMode, loaded.CardCompactMode);
        Assert.Equal(original.CardBackgroundBar, loaded.CardBackgroundBar);
    }
}
