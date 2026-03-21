// <copyright file="StartupPreferencesServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;
using AIUsageTracker.UI.Slim.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.UI;

public class StartupPreferencesServiceTests
{
    [Fact]
    public async Task LoadAndApplyAsync_WhenStoreSucceeds_AppliesThemeAndReturnsPreferencesAsync()
    {
        var expected = new AppPreferences
        {
            Theme = AppTheme.Nord,
            IsPrivacyMode = true,
        };
        var store = new FakePreferencesStore(() => Task.FromResult(expected));
        var themeService = new RecordingThemeService();
        var sut = new StartupPreferencesService(
            store,
            themeService,
            NullLogger<StartupPreferencesService>.Instance);

        var result = await sut.LoadAndApplyAsync();

        Assert.Same(expected, result);
        Assert.Single(themeService.AppliedThemes);
        Assert.Equal(AppTheme.Nord, themeService.AppliedThemes[0]);
    }

    [Fact]
    public async Task LoadAndApplyAsync_WhenStoreThrows_UsesDefaultsAndFallbackThemeAsync()
    {
        var store = new FakePreferencesStore(() => throw new InvalidOperationException("boom"));
        var themeService = new RecordingThemeService();
        var sut = new StartupPreferencesService(
            store,
            themeService,
            NullLogger<StartupPreferencesService>.Instance);

        var result = await sut.LoadAndApplyAsync();

        Assert.NotNull(result);
        Assert.False(result.IsPrivacyMode);
        Assert.Single(themeService.AppliedThemes);
        Assert.Equal(AppTheme.Dark, themeService.AppliedThemes[0]);
    }

    private sealed class FakePreferencesStore : IUiPreferencesStore
    {
        private readonly Func<Task<AppPreferences>> _loadFunc;

        public FakePreferencesStore(Func<Task<AppPreferences>> loadFunc)
        {
            this._loadFunc = loadFunc;
        }

        public Task<AppPreferences> LoadAsync()
        {
            return this._loadFunc();
        }

        public Task<bool> SaveAsync(AppPreferences preferences)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class RecordingThemeService : IAppThemeService
    {
        public List<AppTheme> AppliedThemes { get; } = new();

        public void ApplyTheme(AppTheme theme)
        {
            this.AppliedThemes.Add(theme);
        }
    }
}
