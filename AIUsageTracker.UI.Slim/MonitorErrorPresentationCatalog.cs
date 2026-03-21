// <copyright file="MonitorErrorPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class MonitorErrorPresentationCatalog
{
    private const string LaunchHeading = "Monitor failed to start.";
    private const string LaunchFallbackDetails = "Please ensure AIUsageTracker.Monitor is installed and try again.";
    private const string ConnectionHeading = "Cannot connect to Monitor.";
    private const string ConnectionFallbackDetails =
        "Please ensure:\n1. Monitor is running\n2. Port is correct (check monitor.json)\n3. Firewall is not blocking\n\nTry restarting the Monitor.";

    public static string BuildLaunchErrorMessage(IReadOnlyCollection<string> errors)
    {
        return BuildErrorMessage(LaunchHeading, LaunchFallbackDetails, errors);
    }

    public static string BuildConnectionErrorMessage(IReadOnlyCollection<string> errors)
    {
        return BuildErrorMessage(ConnectionHeading, ConnectionFallbackDetails, errors);
    }

    public static string BuildErrorMessage(string heading, string fallbackDetails, IReadOnlyCollection<string>? errors)
    {
        if (errors == null || errors.Count == 0)
        {
            return $"{heading}\n\n{fallbackDetails}";
        }

        var details = string.Join(
            Environment.NewLine,
            errors.Take(3).Select(error => $"- {error}"));

        return $"{heading}\n\nMonitor reported:\n{details}\n\n{fallbackDetails}";
    }
}
