// <copyright file="MainViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.ObjectModel;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.ViewModels;

public class MainViewModel : BaseViewModel
{
    private readonly IMonitorService _monitorService;
    private readonly IUsageAnalyticsService _analyticsService;
    private readonly ILogger<MainViewModel> _logger;
    private bool _isLoading;
    private bool _isPrivacyMode;
    private ObservableCollection<ProviderUsage> _usages = new();
    private DateTime _lastRefreshTime = DateTime.MinValue;
    private string _statusMessage = "Initializing...";

    public MainViewModel(
        IMonitorService monitorService,
        IUsageAnalyticsService analyticsService,
        ILogger<MainViewModel> logger)
    {
        this._monitorService = monitorService;
        this._analyticsService = analyticsService;
        this._logger = logger;
        this._isPrivacyMode = false; // Initial state
    }

    public bool IsLoading
    {
        get => this._isLoading;
        set => this.SetProperty(ref this._isLoading, value);
    }

    public bool IsPrivacyMode
    {
        get => this._isPrivacyMode;
        set => this.SetProperty(ref this._isPrivacyMode, value);
    }

    public string StatusMessage
    {
        get => this._statusMessage;
        set => this.SetProperty(ref this._statusMessage, value);
    }

    public ObservableCollection<ProviderUsage> Usages
    {
        get => this._usages;
        private set => this.SetProperty(ref this._usages, value);
    }

    public DateTime LastRefreshTime
    {
        get => this._lastRefreshTime;
        private set => this.SetProperty(ref this._lastRefreshTime, value);
    }

    public async Task RefreshDataAsync()
    {
        if (this.IsLoading)
        {
            return;
        }

        this.IsLoading = true;
        this.StatusMessage = "Refreshing data...";
        try
        {
            await this._monitorService.RefreshPortAsync().ConfigureAwait(false);
            var results = await this._monitorService.GetUsageAsync().ConfigureAwait(false);

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

    public void SetPrivacyMode(bool enabled)
    {
        this.IsPrivacyMode = enabled;
    }
}
