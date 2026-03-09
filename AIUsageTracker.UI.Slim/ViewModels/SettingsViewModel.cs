using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.ViewModels;

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
        get => _isPrivacyMode;
        set => SetProperty(ref _isPrivacyMode, value);
    }
`n
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
`n
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }
`n
    public List<ProviderConfig> Configs
    {
        get => _configs;
        private set => SetProperty(ref _configs, value);
    }
`n
    public SettingsViewModel(
        IMonitorService monitorService,
        IUsageAnalyticsService analyticsService,
        IDataExportService exportService,
        ILogger<SettingsViewModel> logger)
    {
        _monitorService = monitorService;
        _analyticsService = analyticsService;
        _exportService = exportService;
        _logger = logger;
    }
`n
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading settings...";
        try
        {
            Configs = (await _monitorService.GetConfigsAsync().ConfigureAwait(false)).ToList();
            _usages = (await _monitorService.GetUsageAsync().ConfigureAwait(false)).ToList();

            if (Configs.Count == 0)
            {
                StatusMessage = "No providers found.";
            }
            else
            {
                StatusMessage = $"Loaded {Configs.Count} providers.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings in ViewModel");
            StatusMessage = "Error loading settings.";
        }
        finally
        {
            IsLoading = false;
        }
    }
`n
    public void TogglePrivacyMode()
    {
        IsPrivacyMode = !IsPrivacyMode;
        StatusMessage = IsPrivacyMode ? "Privacy Mode Enabled" : "Privacy Mode Disabled";
    }
`n
    public async Task<string> ExportDataAsync()
    {
        return await _exportService.ExportHistoryToCsvAsync().ConfigureAwait(false);
    }
}
