using Microsoft.Extensions.Logging;
using AIUsageTracker.Core.Interfaces;

namespace AIUsageTracker.Infrastructure.Services;

public class WindowsNotificationService : INotificationService
{
    private readonly ILogger<WindowsNotificationService> _logger;
    private bool _isInitialized;

    public WindowsNotificationService(ILogger<WindowsNotificationService> logger)
    {
        _logger = logger;
    }

    public void Initialize()
    {
        if (_isInitialized) return;

        _logger.LogInformation("Initializing notification service");
        _isInitialized = true;
    }

    public void Unregister()
    {
        if (!_isInitialized) return;

        _isInitialized = false;
    }

    public void ShowNotification(string title, string message, string? action = null, string? argument = null)
    {
        _logger.LogInformation("Notification: {Title} - {Message}", title, message);
    }

    public void ShowUsageAlert(string providerName, double usagePercentage)
    {
        var level = usagePercentage switch
        {
            >= 90 => "Critical",
            >= 75 => "Warning",
            _ => "Info"
        };
        var message = usagePercentage >= 100
            ? $"Quota exceeded at {usagePercentage:F1}%."
            : $"Usage is {usagePercentage:F1}%";

        ShowNotification($"{providerName} - {level}", message, "showProvider", providerName);
    }

    public void ShowQuotaExceeded(string providerName, string details)
    {
        ShowNotification($"{providerName} Quota Exceeded", details, "showProvider", providerName);
    }

    public event EventHandler<NotificationClickedEventArgs>? OnNotificationClicked
    {
        add { }
        remove { }
    }
}


