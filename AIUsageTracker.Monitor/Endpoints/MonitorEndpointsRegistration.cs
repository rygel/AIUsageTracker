// <copyright file="MonitorEndpointsRegistration.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Endpoints
{
    using Microsoft.AspNetCore.Builder;

    internal static class MonitorEndpointsRegistration
    {
        public static void MapAll(
            WebApplication app,
            bool isDebugMode,
            int port,
            string agentVersion,
            string apiContractVersion,
            string[] args)
        {
            MonitorDiagnosticsEndpoints.Map(
                app,
                isDebugMode,
                port,
                agentVersion,
                apiContractVersion,
                args);
            MonitorUsageEndpoints.Map(app);
            MonitorConfigEndpoints.Map(app);
            MonitorHistoryEndpoints.Map(app);
        }
    }
}
