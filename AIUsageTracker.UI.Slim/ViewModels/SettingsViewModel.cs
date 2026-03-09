// <copyright file="SettingsViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim.ViewModels
{
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Core.MonitorClient;
    using Microsoft.Extensions.Logging;

    public class SettingsViewModel : BaseViewModel
    {
        private readonly IMonitorService _monitorService;
        private readonly IUsageAnalyticsService _analyticsService;
        private readonly IDataExportService _exportService;
        private readonly ILogger<SettingsViewModel> _logger;
        private bool _isPrivacyMode;
        private string _statusMessage = "Ready";
        private bool _isLoading;
        private List<ProviderConfig> _configs = new();
        private List<ProviderUsage> _usages = new();

        public bool IsPrivacyMode
        {
            get => this._isPrivacyMode;
            set => this.SetProperty(ref this._isPrivacyMode, value);
        }
    `n
        public string StatusMessage
        {
            get => this._statusMessage;
            set => this.SetProperty(ref this._statusMessage, value);
        }
    `n
        public bool IsLoading
        {
            get => this._isLoading;
            set => this.SetProperty(ref this._isLoading, value);
        }
    `n
        public List<ProviderConfig> Configs
        {
            get => this._configs;
            private set => this.SetProperty(ref this._configs, value);
        }
    `n
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
    `n
        public async Task LoadDataAsync()
        {
            this.IsLoading = true;
            this.StatusMessage = "Loading settings...";
            try
            {
                this.Configs = (await this._monitorService.GetConfigsAsync().ConfigureAwait(false)).ToList();
                this._usages = (await this._monitorService.GetUsageAsync().ConfigureAwait(false)).ToList();

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
    `n
        public void TogglePrivacyMode()
        {
            this.IsPrivacyMode = !this.IsPrivacyMode;
            this.StatusMessage = this.IsPrivacyMode ? "Privacy Mode Enabled" : "Privacy Mode Disabled";
        }
    `n
        public async Task<string> ExportDataAsync()
        {
            return await this._exportService.ExportHistoryToCsvAsync().ConfigureAwait(false);
        }
    }
}
