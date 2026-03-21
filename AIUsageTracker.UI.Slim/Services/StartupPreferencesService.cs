// <copyright file="StartupPreferencesService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

public sealed class StartupPreferencesService : IStartupPreferencesService
{
    private readonly IUiPreferencesStore _preferencesStore;
    private readonly IAppThemeService _themeService;
    private readonly ILogger<StartupPreferencesService> _logger;

    public StartupPreferencesService(
        IUiPreferencesStore preferencesStore,
        IAppThemeService themeService,
        ILogger<StartupPreferencesService> logger)
    {
        this._preferencesStore = preferencesStore;
        this._themeService = themeService;
        this._logger = logger;
    }

    public async Task<AppPreferences> LoadAndApplyAsync()
    {
        try
        {
            var preferences = await this._preferencesStore.LoadAsync().ConfigureAwait(false);
            this._themeService.ApplyTheme(preferences.Theme);
            return preferences;
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to load/apply Slim preferences on startup.");
            UiDiagnosticFileLog.Write($"[DIAGNOSTIC] Failed to load preferences on startup: {ex.Message}");

            try
            {
                this._themeService.ApplyTheme(AppTheme.Dark);
            }
            catch (Exception themeEx)
            {
                this._logger.LogWarning(themeEx, "Failed to apply fallback startup theme.");
            }

            return new AppPreferences();
        }
    }
}
