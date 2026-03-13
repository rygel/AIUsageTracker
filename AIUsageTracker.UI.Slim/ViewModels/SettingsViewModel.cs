// <copyright file="SettingsViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.ViewModels;

/// <summary>
/// ViewModel for the settings window, managing provider configurations and preferences.
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
    private readonly IMonitorService _monitorService;
    private readonly IUsageAnalyticsService _analyticsService;
    private readonly IDataExportService _exportService;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    private bool _isPrivacyMode;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private IReadOnlyList<ProviderConfig> _configs = Array.Empty<ProviderConfig>();

    [ObservableProperty]
    private List<ProviderUsage> _usages = new();

    public SettingsViewModel(
        IMonitorService monitorService,
        IUsageAnalyticsService analyticsService,
        IDataExportService exportService,
        ILogger<SettingsViewModel> logger)
    {
        this._monitorService = monitorService;
        this._analyticsService = analyticsService;
        this._exportService = exportService;
        this._logger = logger;
    }

    [RelayCommand]
    internal async Task LoadDataAsync()
    {
        this.IsLoading = true;
        this.StatusMessage = "Loading settings...";
        try
        {
            this.Configs = (await this._monitorService.GetConfigsAsync().ConfigureAwait(true)).ToList();
            this.Usages = (await this._monitorService.GetUsageAsync().ConfigureAwait(true)).ToList();

            if (this.Configs.Count == 0)
            {
                this.StatusMessage = "No providers found.";
            }
            else
            {
                this.StatusMessage = $"Loaded {this.Configs.Count} providers.";
            }
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to load settings in ViewModel");
            this.StatusMessage = "Error loading settings.";
        }
        finally
        {
            this.IsLoading = false;
        }
    }

    [RelayCommand]
    internal void TogglePrivacyMode()
    {
        this.IsPrivacyMode = !this.IsPrivacyMode;
        this.StatusMessage = this.IsPrivacyMode ? "Privacy Mode Enabled" : "Privacy Mode Disabled";
    }

    [RelayCommand]
    private async Task<string> ExportDataAsync()
    {
        return await this._exportService.ExportHistoryToCsvAsync().ConfigureAwait(true);
    }
}
