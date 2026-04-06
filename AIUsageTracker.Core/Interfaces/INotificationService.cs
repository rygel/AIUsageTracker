// <copyright file="INotificationService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Interfaces;

public interface INotificationService
{
    event EventHandler<NotificationClickedEventArgs>? OnNotificationClicked;

    void ShowNotification(string title, string message, string? action = null, string? argument = null);

    void ShowUsageAlert(string providerName, double usagePercentage);

    void ShowQuotaExceeded(string providerName, string details);

    void ShowSubscriptionExpired(string providerName);

    void Initialize();

    void Unregister();
}
