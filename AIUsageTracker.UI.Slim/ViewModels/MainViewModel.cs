using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace AIUsageTracker.UI.Slim.ViewModels;

public class MainViewModel : BaseViewModel
{
    private readonly IMonitorService _monitorService;
    private readonly ILogger<MainViewModel> _logger;
    private bool _isLoading;
    private bool _isPrivacyMode;
    private ObservableCollection<ProviderUsage> _usages = new();
    private DateTime _lastRefreshTime = DateTime.MinValue;
    private string _statusMessage = "Initializing...";

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsPrivacyMode
    {
        get => _isPrivacyMode;
        set => SetProperty(ref _isPrivacyMode, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ObservableCollection<ProviderUsage> Usages
    {
        get => _usages;
        private set => SetProperty(ref _usages, value);
    }

    public DateTime LastRefreshTime
    {
        get => _lastRefreshTime;
        private set => SetProperty(ref _lastRefreshTime, value);
    }

    public MainViewModel(IMonitorService monitorService, ILogger<MainViewModel> logger)
    {
        _monitorService = monitorService;
        _logger = logger;
        _isPrivacyMode = false; // Initial state
    }

    public async Task RefreshDataAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusMessage = "Refreshing data...";
        try
        {
            await _monitorService.RefreshPortAsync();
            var results = await _monitorService.GetUsageAsync();
            
            Usages.Clear();
            foreach (var usage in results)
            {
                Usages.Add(usage);
            }

            LastRefreshTime = DateTime.Now;
            StatusMessage = results.Any() ? "Data updated" : "No active providers found";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh data in MainViewModel");
            StatusMessage = "Connection failed";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SetPrivacyMode(bool enabled)
    {
        IsPrivacyMode = enabled;
    }
}
