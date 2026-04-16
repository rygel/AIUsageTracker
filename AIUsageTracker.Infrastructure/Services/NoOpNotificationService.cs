// <copyright file="NoOpNotificationService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;

using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Services;

public class NoOpNotificationService : INotificationService
{
    private readonly ILogger<NoOpNotificationService> _logger;

    public NoOpNotificationService(ILogger<NoOpNotificationService> logger)
    {
        this._logger = logger;
    }

    public event EventHandler<NotificationClickedEventArgs>? OnNotificationClicked
    {
        add { _ = value; } // Intentionally ignored - no-op notification service
        remove { _ = value; } // Intentionally ignored - no-op notification service
    }

    public void Initialize()
    {
        this._logger.LogInformation("Notifications are disabled on this platform.");
    }

    public void Unregister()
    {
    }

    public void ShowNotification(string title, string message, string? action = null, string? argument = null)
    {
        this._logger.LogDebug("Notification (no-op): {Title} - {Message}", title, message);
    }

    public void ShowUsageAlert(string providerName, double usagePercentage)
    {
        this._logger.LogDebug("Usage alert (no-op): {Provider} at {Percentage:F1}%", providerName, usagePercentage);
    }

    public void ShowQuotaExceeded(string providerName, string details)
    {
        this._logger.LogDebug("Quota exceeded alert (no-op): {Provider} - {Details}", providerName, details);
    }

    public void ShowSubscriptionExpired(string providerName)
    {
        this._logger.LogDebug("Subscription expired alert (no-op): {Provider}", providerName);
    }
}
