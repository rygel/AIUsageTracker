namespace AIUsageTracker.Core.Models;

/// <summary>
/// Represents arguments passed to notification click event handlers.
/// </summary>
public class NotificationClickedEventArgs : EventArgs
{
    public string Action { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
}
