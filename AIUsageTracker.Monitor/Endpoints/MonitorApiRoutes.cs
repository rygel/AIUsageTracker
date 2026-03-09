// <copyright file="MonitorApiRoutes.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Endpoints
{
    internal static class MonitorApiRoutes
    {
        public const string Health = "/api/health";
        public const string Diagnostics = "/api/diagnostics";
        public const string Usage = "/api/usage";
        public const string UsageByProvider = "/api/usage/{providerId}";
        public const string Refresh = "/api/refresh";
        public const string NotificationTest = "/api/notifications/test";
        public const string Config = "/api/config";
        public const string ConfigByProvider = "/api/config/{providerId}";
        public const string ScanKeys = "/api/scan-keys";
        public const string History = "/api/history";
        public const string HistoryByProvider = "/api/history/{providerId}";
        public const string ResetsByProvider = "/api/resets/{providerId}";
    }
}
