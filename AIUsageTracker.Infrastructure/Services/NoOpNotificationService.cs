using AIUsageTracker.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Services;

public class NoOpNotificationService : INotificationService
{
    private readonly ILogger<NoOpNotificationService> _logger;

    public NoOpNotificationService(ILogger<NoOpNotificationService> logger)
    {
        _logger = logger;
    }

    public event EventHandler<NotificationClickedEventArgs>? OnNotificationClicked
    {
        add { }
        remove { }
    }

    public void Initialize()
    {
        _logger.LogInformation("Notifications are disabled on this platform.");
    }

    public void Unregister()
    {
    }

    public void ShowNotification(string title, string message, string? action = null, string? argument = null)
    {
        _logger.LogDebug("Notification (no-op): {Title} - {Message}", title, message);
    }

    public void ShowUsageAlert(string providerName, double usagePercentage)
    {
        _logger.LogDebug("Usage alert (no-op): {Provider} at {Percentage:F1}%", providerName, usagePercentage);
    }

    public void ShowQuotaExceeded(string providerName, string details)
    {
        _logger.LogDebug("Quota exceeded alert (no-op): {Provider} - {Details}", providerName, details);
    }
}
