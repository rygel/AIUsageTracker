namespace AIUsageTracker.Core.Interfaces;

public interface INotificationService
{
    void ShowNotification(string title, string message, string? action = null, string? argument = null);
    void ShowUsageAlert(string providerName, double usagePercentage);
    void ShowQuotaExceeded(string providerName, string details);
    void Initialize();
    void Unregister();
    event EventHandler<NotificationClickedEventArgs>? OnNotificationClicked;
}

public class NotificationClickedEventArgs : EventArgs
{
    public string Action { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
}

