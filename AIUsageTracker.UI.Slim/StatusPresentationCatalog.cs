// <copyright file="StatusPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

internal static class StatusPresentationCatalog
{
    public static StatusPresentation Create(
        string message,
        StatusType type,
        string? monitorContractWarningMessage,
        DateTime lastMonitorUpdate)
    {
        var effectiveMessage = message;
        var effectiveType = type;
        if (effectiveType == StatusType.Success &&
            !string.IsNullOrWhiteSpace(monitorContractWarningMessage))
        {
            effectiveMessage = monitorContractWarningMessage;
            effectiveType = StatusType.Warning;
        }

        var tooltipText = lastMonitorUpdate == DateTime.MinValue
            ? "Last update: Never"
            : $"Last update: {lastMonitorUpdate:HH:mm:ss}";

        var indicatorKind = effectiveType switch
        {
            StatusType.Success => StatusIndicatorKind.Success,
            StatusType.Warning => StatusIndicatorKind.Warning,
            StatusType.Error => StatusIndicatorKind.Error,
            _ => StatusIndicatorKind.Neutral,
        };

        var logLevel = effectiveType switch
        {
            StatusType.Error => LogLevel.Error,
            StatusType.Warning => LogLevel.Warning,
            _ => LogLevel.Information,
        };

        return new StatusPresentation(
            Message: effectiveMessage,
            Type: effectiveType,
            IndicatorKind: indicatorKind,
            TooltipText: tooltipText,
            LogLevel: logLevel);
    }
}
