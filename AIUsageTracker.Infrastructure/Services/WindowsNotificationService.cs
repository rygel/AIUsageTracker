// <copyright file="WindowsNotificationService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using AIUsageTracker.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Services;

public class WindowsNotificationService : INotificationService
{
    private readonly ILogger<WindowsNotificationService> _logger;
    private bool _isInitialized;

    public WindowsNotificationService(ILogger<WindowsNotificationService> logger)
    {
        this._logger = logger;
    }

    public event EventHandler<NotificationClickedEventArgs>? OnNotificationClicked
    {
        add { _ = value; } // Intentionally ignored - notification failure is non-critical
        remove { _ = value; } // Intentionally ignored - notification failure is non-critical
    }

    public void Initialize()
    {
        if (this._isInitialized)
        {
            return;
        }

        this._logger.LogInformation("Initializing notification service");
        this._isInitialized = true;
    }

    public void Unregister()
    {
        if (!this._isInitialized)
        {
            return;
        }

        this._isInitialized = false;
    }

    public void ShowNotification(string title, string message, string? action = null, string? argument = null)
    {
        this._logger.LogInformation("Notification: {Title} - {Message}", title, message);
    }

    public void ShowUsageAlert(string providerName, double usagePercentage)
    {
        var level = usagePercentage switch
        {
            >= 90 => "Critical",
            >= 75 => "Warning",
            _ => "Info",
        };
        var message = usagePercentage >= 100
            ? $"Quota exceeded at {usagePercentage.ToString("F1", CultureInfo.InvariantCulture)}%."
            : $"Usage is {usagePercentage.ToString("F1", CultureInfo.InvariantCulture)}%";

        this.ShowNotification($"{providerName} - {level}", message, "showProvider", providerName);
    }

    public void ShowQuotaExceeded(string providerName, string details)
    {
        this.ShowNotification($"{providerName} Quota Exceeded", details, "showProvider", providerName);
    }

    public void ShowSubscriptionExpired(string providerName)
    {
        this.ShowNotification(
            $"{providerName} — Subscription Expired",
            "Your subscription has expired. Renew to continue using this provider.",
            "showProvider",
            providerName);
    }
}
