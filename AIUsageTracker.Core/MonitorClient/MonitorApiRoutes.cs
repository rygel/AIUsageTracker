// <copyright file="MonitorApiRoutes.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;

namespace AIUsageTracker.Core.MonitorClient;

internal static class MonitorApiRoutes
{
    public const string Usage = "/api/usage";
    public const string History = "/api/history";
    public const string Refresh = "/api/refresh";
    public const string Config = "/api/config";
    public const string TestNotification = "/api/notifications/test";
    public const string ScanKeys = "/api/scan-keys";
    public const string Health = "/api/health";
    public const string Diagnostics = "/api/diagnostics";
    public const string Export = "/api/export";

    public static string UsageByProvider(string providerId) =>
        $"/api/usage/{EscapePathSegment(providerId)}";

    public static string HistoryWithLimit(int limit) =>
        $"{History}?limit={limit.ToString(CultureInfo.InvariantCulture)}";

    public static string HistoryByProviderWithLimit(string providerId, int limit) =>
        $"/api/history/{EscapePathSegment(providerId)}?limit={limit.ToString(CultureInfo.InvariantCulture)}";

    public static string ConfigByProvider(string providerId) =>
        $"/api/config/{EscapePathSegment(providerId)}";

    public static string ProviderCheck(string providerId) =>
        $"/api/providers/{EscapePathSegment(providerId)}/check";

    public static string ExportByFormat(string format) =>
        $"/api/export/{EscapePathSegment(format)}";

    public static string ExportWithWindow(string format, int days) =>
        $"/api/export?format={Uri.EscapeDataString(format)}&days={days.ToString(CultureInfo.InvariantCulture)}";

    private static string EscapePathSegment(string value) => Uri.EscapeDataString(value);
}
