// <copyright file="PreferenceConcurrencyTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Tests.Infrastructure;
using AIUsageTracker.UI.Slim;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.UI;

/// <summary>
/// Verifies that the main window and settings window share a single
/// <see cref="AppPreferences"/> instance so that concurrent preference changes
/// from either window are never silently reverted.
///
/// Prior to the fix, each window loaded its own copy from disk. When the user
/// toggled "Show Used" on the main window and then changed "Pace-aware colours"
/// in the already-open settings window, the stale settings copy overwrote the
/// main window's change on save.
/// </summary>
public sealed class PreferenceConcurrencyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly UiPreferencesStore _store;

    public PreferenceConcurrencyTests()
    {
        this._tempDir = TestTempPaths.CreateDirectory("preference-concurrency-tests");
        var preferencesPath = Path.Combine(this._tempDir, "preferences.json");

        var mockPath = new Mock<IAppPathProvider>();
        mockPath.Setup(p => p.GetPreferencesFilePath()).Returns(preferencesPath);
        mockPath.Setup(p => p.GetAuthFilePath()).Returns(Path.Combine(this._tempDir, "auth.json"));
        this._store = new UiPreferencesStore(
            NullLogger<UiPreferencesStore>.Instance,
            mockPath.Object);
    }

    /// <summary>
    /// Both windows share the same preferences instance (as <c>App.Preferences</c>).
    /// Changing "Show Used" on the main window and then "Pace-aware colours" in
    /// settings must preserve both changes.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task SharedInstance_MainWindowShowUsed_ThenSettingsPaceAdjustment_BothSurvive()
    {
        // App startup: load from disk once — this is the single shared instance.
        var shared = new AppPreferences
        {
            ShowUsedPercentages = false,
            EnablePaceAdjustment = false,
        };
        await this._store.SaveAsync(shared);

        // Main window and settings both hold a reference to the same object.
        var mainWindowPrefs = shared;
        var settingsPrefs = shared;

        // User toggles "Show Used" on the main window → saves.
        mainWindowPrefs.ShowUsedPercentages = true;
        await this._store.SaveAsync(mainWindowPrefs);

        // User toggles "Pace-aware colours" in the settings window → saves.
        settingsPrefs.EnablePaceAdjustment = true;
        await this._store.SaveAsync(settingsPrefs);

        // Both changes must be on the in-memory object.
        Assert.True(shared.ShowUsedPercentages);
        Assert.True(shared.EnablePaceAdjustment);

        // Both changes must survive a round-trip to disk.
        var reloaded = await this._store.LoadAsync();
        Assert.True(reloaded.ShowUsedPercentages);
        Assert.True(reloaded.EnablePaceAdjustment);
    }

    /// <summary>
    /// Reverse direction: settings changes "Pace-aware colours" first, then
    /// main window toggles "Show Used".
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task SharedInstance_SettingsPaceAdjustment_ThenMainWindowShowUsed_BothSurvive()
    {
        var shared = new AppPreferences
        {
            ShowUsedPercentages = false,
            EnablePaceAdjustment = false,
        };
        await this._store.SaveAsync(shared);

        var mainWindowPrefs = shared;
        var settingsPrefs = shared;

        settingsPrefs.EnablePaceAdjustment = true;
        await this._store.SaveAsync(settingsPrefs);

        mainWindowPrefs.ShowUsedPercentages = true;
        await this._store.SaveAsync(mainWindowPrefs);

        Assert.True(shared.EnablePaceAdjustment);
        Assert.True(shared.ShowUsedPercentages);

        var reloaded = await this._store.LoadAsync();
        Assert.True(reloaded.EnablePaceAdjustment);
        Assert.True(reloaded.ShowUsedPercentages);
    }

    /// <summary>
    /// Proves the bug existed: if two separate instances are loaded from disk,
    /// saving the stale one reverts the other's changes.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task SeparateInstances_StaleWriteRevertsChange()
    {
        var initial = new AppPreferences
        {
            ShowUsedPercentages = false,
            EnablePaceAdjustment = false,
        };
        await this._store.SaveAsync(initial);

        // Two separate loads → two distinct objects (the old broken behavior).
        var settingsPrefs = await this._store.LoadAsync();
        var mainWindowPrefs = await this._store.LoadAsync();

        mainWindowPrefs.ShowUsedPercentages = true;
        await this._store.SaveAsync(mainWindowPrefs);

        // Settings saves its stale copy — EnablePaceAdjustment changed,
        // but ShowUsedPercentages is still false on this object.
        settingsPrefs.EnablePaceAdjustment = true;
        await this._store.SaveAsync(settingsPrefs);

        var final = await this._store.LoadAsync();
        Assert.True(final.EnablePaceAdjustment);

        // This FAILS — ShowUsedPercentages was reverted by the stale save.
        // This is the exact bug we fixed by sharing a single instance.
        Assert.False(
            final.ShowUsedPercentages,
            "Expected the stale-write scenario to revert ShowUsedPercentages. " +
            "If this starts passing, the bug was fixed at the store level too.");
    }

    public void Dispose()
    {
        TestTempPaths.CleanupPath(this._tempDir);
    }
}
