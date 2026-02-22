using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
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

        try
        {
            _logger.LogInformation("Initializing Windows Notification Service");

            // Subscribe to notification activation
            ToastNotificationManagerCompat.OnActivated += OnToastActivated;

            // Check if app was launched from a notification
            if (ToastNotificationManagerCompat.WasCurrentProcessToastActivated())
            {
                _logger.LogDebug("App was launched from a toast notification");
            }

            _isInitialized = true;
            _logger.LogInformation("Windows Notification Service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Windows Notification Service");
        }
    }

    public void Unregister()
    {
        if (!_isInitialized) return;

        try
        {
            _logger.LogInformation("Unregistering Windows Notification Service");
            ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
            ToastNotificationManagerCompat.Uninstall();
            _isInitialized = false;
            _logger.LogInformation("Windows Notification Service unregistered");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unregistering Windows Notification Service");
        }
    }

    public void ShowNotification(string title, string message, string? action = null, string? argument = null)
    {
        try
        {
            _logger.LogDebug("Showing notification: {Title} - {Message}", title, message);

            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(message);

            if (!string.IsNullOrEmpty(action) && !string.IsNullOrEmpty(argument))
            {
                builder.AddArgument("action", action)
                       .AddArgument("data", argument);
            }

            builder.Show();
            _logger.LogDebug("Notification displayed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show notification: {Title}", title);
        }
    }

    public void ShowUsageAlert(string providerName, double usagePercentage)
    {
        try
        {
            var level = usagePercentage switch
            {
                >= 90 => "Critical",
                >= 75 => "Warning",
                _ => "Info"
            };

            var color = usagePercentage switch
            {
                >= 90 => "🔴",
                >= 75 => "🟡",
                _ => "🔵"
            };

            var title = $"{color} {providerName} - {level}";
            var message = usagePercentage >= 100
                ? $"Quota exceeded! You've used {usagePercentage:F1}% of your limit."
                : $"You've used {usagePercentage:F1}% of your {providerName} quota.";

            ShowNotification(title, message, "showProvider", providerName);

            _logger.LogInformation("Usage alert shown for {Provider}: {Percentage}%", providerName, usagePercentage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show usage alert for {Provider}", providerName);
        }
    }

    public void ShowQuotaExceeded(string providerName, string details)
    {
        try
        {
            var title = $"🔴 {providerName} Quota Exceeded";
            var message = $"You've exceeded your quota. {details}";

            ShowNotification(title, message, "showProvider", providerName);

            _logger.LogWarning("Quota exceeded notification shown for {Provider}", providerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show quota exceeded notification for {Provider}", providerName);
        }
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat toastArgs)
    {
        try
        {
            _logger.LogDebug("Toast notification activated with arguments: {Arguments}", toastArgs.Argument);

            // Parse the toast arguments
            var args = ToastArguments.Parse(toastArgs.Argument);

            if (args.TryGetValue("action", out var action))
            {
                var data = args.TryGetValue("data", out var value) ? value : string.Empty;
                
                _logger.LogInformation("Notification clicked - Action: {Action}, Data: {Data}", action, data);
                
                // Raise event for UI layer to handle
                OnNotificationClicked?.Invoke(this, new NotificationClickedEventArgs
                {
                    Action = action,
                    Data = data
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling toast activation");
        }
    }

    public event EventHandler<NotificationClickedEventArgs>? OnNotificationClicked;
}


