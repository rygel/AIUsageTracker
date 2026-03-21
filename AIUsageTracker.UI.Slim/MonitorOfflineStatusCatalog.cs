// <copyright file="MonitorOfflineStatusCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class MonitorOfflineStatusCatalog
{
    public static string Format(DateTime lastMonitorUpdate, DateTime now)
    {
        if (lastMonitorUpdate == DateTime.MinValue)
        {
            return "Monitor offline — no data received yet";
        }

        var elapsed = now - lastMonitorUpdate;
        var ago = elapsed.TotalSeconds < 60
            ? $"{(int)elapsed.TotalSeconds}s ago"
            : elapsed.TotalHours < 1
                ? $"{(int)elapsed.TotalMinutes}m ago"
                : $"{(int)elapsed.TotalHours}h ago";

        return $"Monitor offline — last sync {ago}";
    }
}
