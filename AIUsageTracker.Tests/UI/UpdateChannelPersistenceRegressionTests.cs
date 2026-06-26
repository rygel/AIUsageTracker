// <copyright file="UpdateChannelPersistenceRegressionTests.cs" company="AIUsageTracker">
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

/// <summary>
/// CRITICAL REGRESSION GUARD — UpdateChannel must survive app updates and
/// a single bad property must NEVER wipe all preferences.
///
/// Regression history:
///   - v2.3.6-beta.9: User set UpdateChannel to Beta, installed the beta
///     update, and the channel silently reverted to Stable. ROOT CAUSE:
///     Merge commit 8eb7c163 silently dropped [JsonConverter(typeof(
///     JsonStringEnumConverter&lt;UpdateChannel&gt;))] from AppPreferences.cs.
///     Users who had "UpdateChannel": "Beta" (string) in their preferences.json
///     hit a JsonException on startup. PreferencesStore.LoadAsync() caught it
///     and returned new AppPreferences() — wiping EVERY setting: theme, fonts,
///     thresholds, window position, notification config, hidden providers.
///     A secondary issue (no force-save before installer launch) was also fixed.
///
/// These tests ensure that:
///   1. UpdateChannel.Beta survives a full save/load round-trip on disk.
///   2. UpdateChannel.Beta survives the schema-migration deserialization path.
///   3. A force-save writes UpdateChannel to disk immediately (no debounce).
///   4. Preferences written to disk BEFORE an installer launch persist
///      and reload correctly on next startup.
///   5. BOTH string ("Beta") and numeric (1) formats deserialize correctly.
///   6. A single corrupt property does NOT wipe all other valid properties.
///
/// If ANY of these tests fail, users will silently lose their update channel
/// setting (or ALL settings) on the next update. Do not weaken or remove them.
/// </summary>
public sealed class UpdateChannelPersistenceRegressionTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _preferencesPath;
    private readonly UiPreferencesStore _store;
    private readonly Mock<IAppPathProvider> _mockPathProvider;

    public UpdateChannelPersistenceRegressionTests()
    {
        this._testDirectory = TestTempPaths.CreateDirectory("UpdateChannelRegression");
        this._preferencesPath = Path.Combine(this._testDirectory, "preferences.json");

        this._mockPathProvider = new Mock<IAppPathProvider>();
        this._mockPathProvider.Setup(p => p.GetPreferencesFilePath()).Returns(this._preferencesPath);
        this._mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(Path.Combine(this._testDirectory, "auth.json"));
        this._mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this._testDirectory);

        this._store = new UiPreferencesStore(NullLogger<UiPreferencesStore>.Instance, this._mockPathProvider.Object);
    }

    public void Dispose()
    {
        TestTempPaths.CleanupPath(this._testDirectory);
    }

    /// <summary>
    /// UpdateChannel.Beta must survive a save → load round-trip.
    /// This is the most basic guarantee: what we write is what we read back.
    /// </summary>
    [Fact]
    public async Task UpdateChannel_Beta_SurvivesRoundTripAsync()
    {
        var preferences = new AppPreferences { UpdateChannel = UpdateChannel.Beta };

        await this._store.SaveAsync(preferences);
        var loaded = await this._store.LoadAsync();

        Assert.Equal(UpdateChannel.Beta, loaded.UpdateChannel);
    }

    /// <summary>
    /// UpdateChannel.Stable must also survive a round-trip (symmetry check).
    /// </summary>
    [Fact]
    public async Task UpdateChannel_Stable_SurvivesRoundTripAsync()
    {
        var preferences = new AppPreferences { UpdateChannel = UpdateChannel.Stable };

        await this._store.SaveAsync(preferences);
        var loaded = await this._store.LoadAsync();

        Assert.Equal(UpdateChannel.Stable, loaded.UpdateChannel);
    }

    /// <summary>
    /// UpdateChannel.Beta must survive the schema-migration path in
    /// AppPreferences.Deserialize. When the JSON has an older SchemaVersion,
    /// Deserialize runs ApplyMigrations — this must NOT reset UpdateChannel.
    /// </summary>
    [Fact]
    public void UpdateChannel_Beta_SurvivesSchemaMigrationDeserialization()
    {
        var json = """
        {
            "UpdateChannel": 1,
            "SchemaVersion": 1,
            "ShowUsedPercentages": true
        }
        """;

        var deserialized = AppPreferences.Deserialize(json);

        Assert.Equal(UpdateChannel.Beta, deserialized.UpdateChannel);
        Assert.Equal(AppPreferences.CurrentSchemaVersion, deserialized.SchemaVersion);
    }

    /// <summary>
    /// When a preferences file was written by an older version that didn't
    /// have UpdateChannel, deserialization defaults to Stable. This test
    /// documents that behavior — it is NOT a bug, but callers must be aware.
    /// </summary>
    [Fact]
    public void UpdateChannel_MissingFromLegacyJson_DefaultsToStable()
    {
        var json = """
        {
            "SchemaVersion": 1,
            "ShowUsedPercentages": true
        }
        """;

        var deserialized = AppPreferences.Deserialize(json);

        Assert.Equal(UpdateChannel.Stable, deserialized.UpdateChannel);
    }

    /// <summary>
    /// The on-disk JSON must contain the UpdateChannel value after a save.
    /// With JsonStringEnumConverter, it writes as string "Beta". This catches
    /// serialization regressions where UpdateChannel might be silently dropped.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WritesUpdateChannelToDiskJson()
    {
        var preferences = new AppPreferences { UpdateChannel = UpdateChannel.Beta };

        await this._store.SaveAsync(preferences);
        var json = await File.ReadAllTextAsync(this._preferencesPath);

        Assert.Contains("\"UpdateChannel\": \"Beta\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// CRITICAL: Simulates the exact update-install scenario.
    ///
    /// 1. User has UpdateChannel = Beta saved on disk.
    /// 2. Force-save runs (as UpdateInstallerHelper now does before launching
    ///    the installer).
    /// 3. App is force-killed by the installer (simulated by just reloading).
    /// 4. New app starts and loads preferences.
    ///
    /// UpdateChannel MUST still be Beta after this sequence.
    /// </summary>
    [Fact]
    public async Task UpdateChannel_Beta_SurvivesForceKillScenarioAsync()
    {
        // Step 1: User has Beta saved
        var originalPrefs = new AppPreferences
        {
            UpdateChannel = UpdateChannel.Beta,
            Theme = AppTheme.Dracula,
            AlwaysOnTop = true,
        };
        await this._store.SaveAsync(originalPrefs);

        // Step 2: Force-save (as UpdateInstallerHelper does before installer launch)
        var prefsForForceSave = await this._store.LoadAsync();
        prefsForForceSave.UpdateChannel = UpdateChannel.Beta;
        await this._store.SaveAsync(prefsForForceSave);

        // Step 3: App is force-killed (nothing to do — file is on disk)

        // Step 4: New app starts, loads preferences
        var reloaded = await this._store.LoadAsync();

        // UpdateChannel MUST be Beta
        Assert.Equal(UpdateChannel.Beta, reloaded.UpdateChannel);
        Assert.Equal(AppTheme.Dracula, reloaded.Theme);
        Assert.True(reloaded.AlwaysOnTop);
    }

    /// <summary>
    /// CRITICAL: Simulates the regression scenario that ACTUALLY broke users.
    ///
    /// Before the fix, preferences were NOT saved before the installer launched.
    /// This test verifies that a force-save at any point captures the correct
    /// UpdateChannel value, proving the fix works: even if the debounced
    /// auto-save timer hasn't fired, the explicit pre-installer save writes
    /// the correct channel to disk.
    /// </summary>
    [Fact]
    public async Task ForceSave_BeforeInstallerLaunch_PreservesUpdateChannel()
    {
        // Simulate: user changed channel in-memory but debounce timer hasn't fired
        var inMemoryPrefs = new AppPreferences { UpdateChannel = UpdateChannel.Beta };

        // Nothing on disk yet (debounce hasn't fired)
        Assert.False(File.Exists(this._preferencesPath));

        // Force-save (this is what UpdateInstallerHelper now does)
        await this._store.SaveAsync(inMemoryPrefs);

        // Verify disk has the correct value
        Assert.True(File.Exists(this._preferencesPath));
        var loaded = await this._store.LoadAsync();
        Assert.Equal(UpdateChannel.Beta, loaded.UpdateChannel);
    }

    /// <summary>
    /// Verifies that switching from Stable to Beta and immediately force-saving
    /// (no debounce delay) correctly persists Beta to disk. This is the exact
    /// scenario that was broken: user changes channel, then updates.
    /// </summary>
    [Fact]
    public async Task SwitchingToBeta_ThenForceSave_PersistsImmediately()
    {
        // Start with Stable on disk
        await this._store.SaveAsync(new AppPreferences { UpdateChannel = UpdateChannel.Stable });

        // User changes to Beta in settings (in-memory only, debounce pending)
        var currentPrefs = await this._store.LoadAsync();
        currentPrefs.UpdateChannel = UpdateChannel.Beta;

        // Force-save before installer (no waiting for debounce)
        await this._store.SaveAsync(currentPrefs);

        // Reload — must be Beta
        var reloaded = await this._store.LoadAsync();
        Assert.Equal(UpdateChannel.Beta, reloaded.UpdateChannel);
    }

    /// <summary>
    /// Full preferences with UpdateChannel.Beta must survive a round-trip
    /// with ALL other settings populated. This catches cases where a migration
    /// or serialization change might reset UpdateChannel as a side effect.
    /// </summary>
    [Fact]
    public async Task FullPreferences_WithBetaChannel_SurviveRoundTrip()
    {
        var original = new AppPreferences
        {
            UpdateChannel = UpdateChannel.Beta,
            Theme = AppTheme.Nord,
            AlwaysOnTop = false,
            ShowUsedPercentages = true,
            ColorThresholdYellow = 55,
            ColorThresholdRed = 85,
            IsPrivacyMode = true,
            FontFamily = "Consolas",
            FontSize = 14,
            EnableNotifications = true,
            NotificationThreshold = 75.0,
            HiddenProviderItemIds = ["provider1", "provider2"],
            SuppressedProviderIds = ["provider3"],
            CollapsedGroupIds = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["quota"] = true,
                ["credits"] = false,
            },
            ShowDualQuotaBars = false,
            DualQuotaSingleBarMode = DualQuotaSingleBarMode.Burst,
            EnablePaceAdjustment = false,
            CardPrimaryBadge = CardSlotContent.UsageRate,
            CardCompactMode = true,
        };

        await this._store.SaveAsync(original);
        var loaded = await this._store.LoadAsync();

        Assert.Equal(UpdateChannel.Beta, loaded.UpdateChannel);
        Assert.Equal(AppTheme.Nord, loaded.Theme);
        Assert.False(loaded.AlwaysOnTop);
        Assert.True(loaded.ShowUsedPercentages);
        Assert.Equal(55, loaded.ColorThresholdYellow);
        Assert.Equal(85, loaded.ColorThresholdRed);
        Assert.True(loaded.IsPrivacyMode);
        Assert.Equal("Consolas", loaded.FontFamily);
        Assert.Equal(14, loaded.FontSize);
        Assert.True(loaded.EnableNotifications);
        Assert.Equal(75.0, loaded.NotificationThreshold);
        Assert.Equal(original.HiddenProviderItemIds, loaded.HiddenProviderItemIds);
        Assert.Equal(original.SuppressedProviderIds, loaded.SuppressedProviderIds);
        Assert.Equal(original.CollapsedGroupIds, loaded.CollapsedGroupIds);
        Assert.False(loaded.ShowDualQuotaBars);
        Assert.Equal(DualQuotaSingleBarMode.Burst, loaded.DualQuotaSingleBarMode);
        Assert.False(loaded.EnablePaceAdjustment);
        Assert.Equal(CardSlotContent.UsageRate, loaded.CardPrimaryBadge);
        Assert.True(loaded.CardCompactMode);
    }

    // ───────────────────────────────────────────────────────────────────
    // ROOT CAUSE TESTS — serialization format mismatch (the actual bug)
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE ROOT CAUSE TEST: A preferences.json written by a version that
    /// had JsonStringEnumConverter (string format) must deserialize correctly
    /// even after the converter is added/removed across versions.
    ///
    /// This simulates the exact scenario that wiped users' settings:
    /// old version wrote "UpdateChannel": "Beta" (string), new version
    /// reads it. Without the converter, this threw JsonException and
    /// PreferencesStore.LoadAsync returned new AppPreferences() — all defaults.
    /// </summary>
    [Fact]
    public void Deserialize_StringFormatUpdateChannel_LoadsBetaCorrectly()
    {
        var json = """
        {
            "UpdateChannel": "Beta",
            "Theme": "Nord",
            "AlwaysOnTop": false,
            "ShowUsedPercentages": true,
            "SchemaVersion": 3
        }
        """;

        var result = AppPreferences.Deserialize(json);

        Assert.Equal(UpdateChannel.Beta, result.UpdateChannel);
        Assert.Equal(AppTheme.Nord, result.Theme);
        Assert.False(result.AlwaysOnTop);
        Assert.True(result.ShowUsedPercentages);
    }

    /// <summary>
    /// Backward compatibility: numeric format must still deserialize correctly.
    /// Users who installed versions WITHOUT the converter have numeric values.
    /// </summary>
    [Fact]
    public void Deserialize_NumericFormatUpdateChannel_LoadsBetaCorrectly()
    {
        var json = """
        {
            "UpdateChannel": 1,
            "SchemaVersion": 3
        }
        """;

        var result = AppPreferences.Deserialize(json);

        Assert.Equal(UpdateChannel.Beta, result.UpdateChannel);
    }

    /// <summary>
    /// Stable channel in string format must also work.
    /// </summary>
    [Fact]
    public void Deserialize_StringFormatStable_LoadsStableCorrectly()
    {
        var json = """
        {
            "UpdateChannel": "Stable",
            "SchemaVersion": 3
        }
        """;

        var result = AppPreferences.Deserialize(json);

        Assert.Equal(UpdateChannel.Stable, result.UpdateChannel);
    }

    /// <summary>
    /// CRITICAL BLAST-RADIUS TEST: A preferences.json where ONE property has
    /// a format that standard deserialization can't handle must NOT wipe ALL
    /// other valid properties.
    ///
    /// This is the test that should have existed from the start. If this test
    /// had been in place, the v2.3.6-beta.9 settings-wipe regression would
    /// have been caught before shipping.
    ///
    /// Scenario: FontSize is an int but the JSON has it as a quoted string
    /// ("14" instead of 14). Standard deserialization throws because numbers
    /// must be JSON numbers by default. The lenient retry in
    /// AppPreferences.Deserialize enables AllowReadingFromString which
    /// handles this. Every OTHER property must survive — no all-defaults.
    /// </summary>
    [Fact]
    public void Deserialize_OneCorruptProperty_DoesNotWipeAllPreferences()
    {
        // FontSize is an int but has a quoted string value — standard
        // deserialization fails. The lenient retry must salvage ALL properties.
        var json = """
        {
            "UpdateChannel": "Beta",
            "Theme": "Nord",
            "AlwaysOnTop": false,
            "ShowUsedPercentages": true,
            "FontFamily": "Consolas",
            "FontSize": "14",
            "ColorThresholdYellow": 55,
            "ColorThresholdRed": 85,
            "EnableNotifications": true,
            "SchemaVersion": 3
        }
        """;

        var result = AppPreferences.Deserialize(json);

        // EVERY property must be preserved — no all-defaults wipe.
        Assert.Equal(UpdateChannel.Beta, result.UpdateChannel);
        Assert.Equal(AppTheme.Nord, result.Theme);
        Assert.False(result.AlwaysOnTop);
        Assert.True(result.ShowUsedPercentages);
        Assert.Equal("Consolas", result.FontFamily);
        Assert.Equal(14, result.FontSize);
        Assert.Equal(55, result.ColorThresholdYellow);
        Assert.Equal(85, result.ColorThresholdRed);
        Assert.True(result.EnableNotifications);
    }

    /// <summary>
    /// Mixed format: some enums as string, some as numeric — all must load.
    /// This simulates a real-world preferences.json that was edited by
    /// different versions over time.
    /// </summary>
    [Fact]
    public void Deserialize_MixedEnumFormats_AllLoadCorrectly()
    {
        var json = """
        {
            "UpdateChannel": "Beta",
            "Theme": 0,
            "DualQuotaSingleBarMode": "Burst",
            "CardPrimaryBadge": 2,
            "SchemaVersion": 3
        }
        """;

        var result = AppPreferences.Deserialize(json);

        Assert.Equal(UpdateChannel.Beta, result.UpdateChannel);
        Assert.Equal(AppTheme.Dark, result.Theme);
        Assert.Equal(DualQuotaSingleBarMode.Burst, result.DualQuotaSingleBarMode);
    }

    /// <summary>
    /// Full end-to-end: write preferences with the current serializer (string
    /// enum format), then manually edit the file to have numeric format, then
    /// reload — UpdateChannel must survive the cross-format round-trip.
    /// </summary>
    [Fact]
    public async Task CrossFormatRoundTrip_NumericOnDisk_LoadsCorrectly()
    {
        // Save with string format (current behavior)
        await this._store.SaveAsync(new AppPreferences
        {
            UpdateChannel = UpdateChannel.Beta,
            Theme = AppTheme.Nord,
            AlwaysOnTop = false,
        });

        // Tamper: replace string enum values with numeric
        var json = await File.ReadAllTextAsync(this._preferencesPath);
        json = json.Replace("\"UpdateChannel\": \"Beta\"", "\"UpdateChannel\": 1", StringComparison.Ordinal);
        json = json.Replace("\"Theme\": \"Nord\"", "\"Theme\": 5", StringComparison.Ordinal);
        await File.WriteAllTextAsync(this._preferencesPath, json);

        // Reload — must still have the correct values
        var loaded = await this._store.LoadAsync();

        Assert.Equal(UpdateChannel.Beta, loaded.UpdateChannel);
        Assert.Equal(AppTheme.Nord, loaded.Theme);
        Assert.False(loaded.AlwaysOnTop);
    }
}
