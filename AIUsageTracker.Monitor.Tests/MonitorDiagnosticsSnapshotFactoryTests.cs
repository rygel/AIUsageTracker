// <copyright file="MonitorDiagnosticsSnapshotFactoryTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Monitor.Endpoints;
using AIUsageTracker.Monitor.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace AIUsageTracker.Monitor.Tests;

public class MonitorDiagnosticsSnapshotFactoryTests
{
    [Fact]
    public void Create_WhenEndpointsContainNonApiRoutes_OnlyReturnsApiEndpointsWithDistinctSortedMethods()
    {
        var dataSource = BuildDataSource(
            CreateRouteEndpoint("/api/usage", ["get", "post"]),
            CreateRouteEndpoint("/api/usage", ["GET"]),
            CreateRouteEndpoint("/api/refresh", ["post"]),
            CreateRouteEndpoint("/health", ["get"]));

        var snapshot = MonitorDiagnosticsSnapshotFactory.Create(
            dataSource,
            port: 5000,
            args: ["--debug"],
            refreshTelemetry: new RefreshTelemetrySnapshot { RefreshCount = 10 },
            schedulerTelemetry: new MonitorJobSchedulerSnapshot { EnqueuedJobs = 5 },
            pipelineTelemetry: new ProviderUsageProcessingTelemetrySnapshot { TotalProcessedEntries = 8 });

        Assert.Equal(2, snapshot.Endpoints.Count);
        Assert.Equal("/api/refresh", snapshot.Endpoints[0].Route);
        Assert.Equal(["POST"], snapshot.Endpoints[0].Methods);
        Assert.Equal("/api/usage", snapshot.Endpoints[1].Route);
        Assert.Equal(["GET", "POST"], snapshot.Endpoints[1].Methods);
        Assert.Equal(
            [MonitorActivitySources.RefreshSourceName, MonitorActivitySources.SchedulerSourceName],
            snapshot.Observability.ActivitySourceNames);
    }

    [Fact]
    public void Create_MapsProvidedTelemetryAndRuntimeFields()
    {
        var refreshTelemetry = new RefreshTelemetrySnapshot
        {
            RefreshCount = 22,
            RefreshSuccessCount = 20,
        };
        var schedulerTelemetry = new MonitorJobSchedulerSnapshot
        {
            TotalQueuedJobs = 3,
            ExecutedJobs = 9,
        };
        var pipelineTelemetry = new ProviderUsageProcessingTelemetrySnapshot
        {
            TotalProcessedEntries = 12,
            LastRunAcceptedEntries = 4,
        };

        var snapshot = MonitorDiagnosticsSnapshotFactory.Create(
            BuildDataSource(CreateRouteEndpoint("/api/usage", ["get"])),
            port: 5010,
            args: ["--port", "5010"],
            refreshTelemetry,
            schedulerTelemetry,
            pipelineTelemetry);

        Assert.Equal(5010, snapshot.Port);
        Assert.True(snapshot.ProcessId > 0);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.WorkingDir));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.BaseDir));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.StartedAt));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Os));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Runtime));
        Assert.Equal(["--port", "5010"], snapshot.Args);
        Assert.Same(refreshTelemetry, snapshot.RefreshTelemetry);
        Assert.Same(schedulerTelemetry, snapshot.SchedulerTelemetry);
        Assert.Same(pipelineTelemetry, snapshot.PipelineTelemetry);
        Assert.Equal(
            [MonitorActivitySources.RefreshSourceName, MonitorActivitySources.SchedulerSourceName],
            snapshot.Observability.ActivitySourceNames);
    }

    private static EndpointDataSource BuildDataSource(params RouteEndpoint[] endpoints)
    {
        return new DefaultEndpointDataSource(endpoints);
    }

    private static RouteEndpoint CreateRouteEndpoint(string route, IReadOnlyList<string> methods)
    {
        var builder = new RouteEndpointBuilder(
            static context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            RoutePatternFactory.Parse(route),
            order: 0);

        builder.Metadata.Add(new HttpMethodMetadata(methods));
        return (RouteEndpoint)builder.Build();
    }
}
