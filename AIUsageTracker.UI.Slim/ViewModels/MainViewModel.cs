// <copyright file="MainViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.ObjectModel;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
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
    private readonly IUsageAnalyticsService _analyticsService;
    private readonly ILogger<MainViewModel> _logger;

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

    public MainViewModel(
        IMonitorService monitorService,
        IUsageAnalyticsService analyticsService,
        ILogger<MainViewModel> logger)
    {
        this._monitorService = monitorService;
        this._analyticsService = analyticsService;
        this._logger = logger;
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

        var renderPreparation = ProviderUsageDisplayCatalog.PrepareForMainWindow(usages.ToList());
        var filteredUsages = renderPreparation.DisplayableUsages;
        var orderedUsages = ProviderMainWindowOrderingCatalog.OrderForMainWindow(filteredUsages);

        // Group by quota-based vs pay-as-you-go
        var quotaUsages = orderedUsages.Where(u => u.IsQuotaBased).ToList();
        var paygoUsages = orderedUsages.Where(u => !u.IsQuotaBased).ToList();

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
                if (ProviderCapabilityCatalog.ShouldRenderAggregateDetailsInMainWindow(usage.ProviderId ?? string.Empty))
                {
                    // For aggregate providers, create cards from details
                    foreach (var modelUsage in ProviderUsageDisplayCatalog.CreateAggregateDetailUsages(usage))
                    {
                        quotaSection.Items.Add(new ProviderCardViewModel(modelUsage, prefs, this.IsPrivacyMode));
                    }
                }
                else
                {
                    quotaSection.Items.Add(new ProviderCardViewModel(usage, prefs, this.IsPrivacyMode));
                }
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
                if (ProviderCapabilityCatalog.ShouldRenderAggregateDetailsInMainWindow(usage.ProviderId ?? string.Empty))
                {
                    foreach (var modelUsage in ProviderUsageDisplayCatalog.CreateAggregateDetailUsages(usage))
                    {
                        paygoSection.Items.Add(new ProviderCardViewModel(modelUsage, prefs, this.IsPrivacyMode));
                    }
                }
                else
                {
                    paygoSection.Items.Add(new ProviderCardViewModel(usage, prefs, this.IsPrivacyMode));
                }
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
