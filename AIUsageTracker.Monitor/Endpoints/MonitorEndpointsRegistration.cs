// <copyright file="MonitorEndpointsRegistration.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Endpoints;

internal static class MonitorEndpointsRegistration
{
    public static void MapAll(
        WebApplication app,
        bool isDebugMode,
        int port,
        string agentVersion,
        string contractVersion,
        string minClientContractVersion,
        string[] args)
    {
        MonitorDiagnosticsEndpoints.Map(
            app,
            isDebugMode,
            port,
            agentVersion,
            contractVersion,
            minClientContractVersion,
            args);
        MonitorUsageEndpoints.Map(app);
        MonitorConfigEndpoints.Map(app);
        MonitorHistoryEndpoints.Map(app);
    }
}
