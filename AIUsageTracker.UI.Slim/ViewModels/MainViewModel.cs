// <copyright file="MainViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.ObjectModel;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.ViewModels;

/// <summary>
/// ViewModel for the main window, managing usage data display and refresh operations.
/// </summary>
public partial class MainViewModel : BaseViewModel
{
    private readonly IMonitorService _monitorService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IBrowserService _browserService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isPrivacyMode;

    [ObservableProperty]
    private string _statusMessage = "Initializing...";

    [ObservableProperty]
    private ObservableCollection<ProviderUsage> _usages = new();

    [ObservableProperty]
    private DateTime _lastRefreshTime = DateTime.MinValue;

    [ObservableProperty]
    private ObservableCollection<CollapsibleSectionViewModel> _sections = new();

    [ObservableProperty]
    private bool _showUsedPercentages;

    [ObservableProperty]
    private bool _enablePaceAdjustment = true;

    [ObservableProperty]
    private bool _isAlwaysOnTop = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    public MainViewModel(
        IMonitorService monitorService,
        ILogger<MainViewModel> logger,
        IBrowserService browserService,
        IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(monitorService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(browserService);
        ArgumentNullException.ThrowIfNull(dialogService);

        this._monitorService = monitorService;
        this._logger = logger;
        this._browserService = browserService;
        this._dialogService = dialogService;
        this._isPrivacyMode = false;
    }

    [RelayCommand]
    internal async Task RefreshDataAsync()
    {
        if (this.IsLoading)
        {
            return;
        }

        this.IsLoading = true;
        this.StatusMessage = "Refreshing data...";
        try
        {
            await this._monitorService.RefreshPortAsync().ConfigureAwait(true);
            var results = await this._monitorService.GetUsageAsync().ConfigureAwait(true);

            this.Usages.Clear();
            foreach (var usage in results)
            {
                this.Usages.Add(usage);
            }

            this.LastRefreshTime = DateTime.Now;
            this.StatusMessage = results.Any() ? "Data updated" : "No active providers found";
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to refresh data in MainViewModel");
            this.StatusMessage = "Connection failed";
        }
        finally
        {
            this.IsLoading = false;
        }
    }

    [RelayCommand]
    private void TogglePrivacyMode()
    {
        this.IsPrivacyMode = !this.IsPrivacyMode;
    }

    /// <summary>
    /// Opens the Web UI in the default browser.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    [RelayCommand]
    internal async Task OpenWebUIAsync()
    {
        try
        {
            await this._browserService.OpenWebUIAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to open Web UI");
            this.StatusMessage = "Failed to open Web UI";
        }
    }

    /// <summary>
    /// Opens the releases page in the default browser.
    /// </summary>
    [RelayCommand]
    internal void ViewChangelog()
    {
        this._browserService.OpenReleasesPage();
    }

    /// <summary>
    /// Opens the settings dialog.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    [RelayCommand]
    internal async Task OpenSettingsAsync()
    {
        try
        {
            var hasChanges = await this._dialogService.ShowSettingsAsync().ConfigureAwait(true);
            if (hasChanges == true)
            {
                // Refresh data after settings change
                await this.RefreshDataAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to open settings");
            this.StatusMessage = "Failed to open settings";
        }
    }

    public void SetPrivacyMode(bool enabled)
    {
        this.IsPrivacyMode = enabled;
        this.UpdatePrivacyModeForSections();
    }

    /// <summary>
    /// Updates the sections collection with structured provider data.
    /// This method organizes providers into collapsible sections by type.
    /// </summary>
    /// <param name="usages">The list of provider usages to display.</param>
    /// <param name="prefs">The application preferences.</param>
    /// <param name="savePreferencesAsync">Function to save preferences when section state changes.</param>
    public void UpdateSections(
        IReadOnlyList<ProviderUsage> usages,
        AppPreferences prefs,
        Func<Task>? savePreferencesAsync = null)
    {
        this.ShowUsedPercentages = prefs.ShowUsedPercentages;
        this.EnablePaceAdjustment = prefs.EnablePaceAdjustment;

        var expandedUsages = MainWindowRuntimeLogic.BuildMainWindowUsageList(
            usages,
            prefs.HiddenProviderItemIds);

        // Group by quota-based vs pay-as-you-go
        var quotaUsages = expandedUsages.Where(u => u.IsQuotaBased).ToList();
        var paygoUsages = expandedUsages.Where(u => !u.IsQuotaBased).ToList();

        this.Sections.Clear();

        if (quotaUsages.Count > 0)
        {
            var quotaSection = new CollapsibleSectionViewModel(
                "Plans & Quotas",
                isQuotaSection: true,
                prefs,
                savePreferencesAsync);

            foreach (var usage in quotaUsages)
            {
                quotaSection.Items.Add(new ProviderCardViewModel(usage, prefs, this.IsPrivacyMode));
            }

            this.Sections.Add(quotaSection);
        }

        if (paygoUsages.Count > 0)
        {
            var paygoSection = new CollapsibleSectionViewModel(
                "Pay As You Go",
                isQuotaSection: false,
                prefs,
                savePreferencesAsync);

            foreach (var usage in paygoUsages)
            {
                paygoSection.Items.Add(new ProviderCardViewModel(usage, prefs, this.IsPrivacyMode));
            }

            this.Sections.Add(paygoSection);
        }
    }

    partial void OnIsPrivacyModeChanged(bool value)
    {
        UpdatePrivacyModeForSections();
    }

    partial void OnShowUsedPercentagesChanged(bool value)
    {
        foreach (var section in Sections)
        {
            foreach (var card in section.Items)
            {
                card.ShowUsedPercentages = value;
            }
        }
    }

    partial void OnEnablePaceAdjustmentChanged(bool value)
    {
        foreach (var section in Sections)
        {
            foreach (var card in section.Items)
            {
                card.EnablePaceAdjustment = value;
            }
        }
    }

    private void UpdatePrivacyModeForSections()
    {
        foreach (var section in this.Sections)
        {
            foreach (var card in section.Items)
            {
                card.IsPrivacyMode = this.IsPrivacyMode;
            }
        }
    }
}

