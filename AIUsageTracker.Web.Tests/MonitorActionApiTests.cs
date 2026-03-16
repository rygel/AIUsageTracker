// <copyright file="MonitorActionApiTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text.Json;

namespace AIUsageTracker.Web.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MonitorActionApiTests
{
    [TestMethod]
    public async Task MonitorStartEndpoint_ReturnsAlreadyRunning_WhenLauncherReportsHealthyMonitorAsync()
    {
        using var host = await TestWebHost.StartAsync(new
        {
            statusSequence = new[]
            {
                CreateStatus(true, 6222, true, "Healthy on port 6222.", error: null),
            },
            ensureAgentRunningResult = false,
            stopAgentResult = false,
        }).ConfigureAwait(false);

        using var response = await host.Client.PostAsync("/api/monitor/start", content: null).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.IsTrue(root.GetProperty("success").GetBoolean());
        Assert.AreEqual("Monitor already running on port 6222.", root.GetProperty("message").GetString());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("startupState").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("startupFailureReason").ValueKind);
    }

    [TestMethod]
    public async Task MonitorStartEndpoint_ReturnsStructuredFailure_WhenLauncherReportsStartupFailureAsync()
    {
        using var host = await TestWebHost.StartAsync(new
        {
            statusSequence = new[]
            {
                CreateStatus(false, 5000, false, "Monitor info file not found. Start Monitor to initialize it.", "agent-info-missing"),
                CreateStatus(false, 5000, true, "Startup status: failed: port bind failed", "monitor-startup-failed"),
            },
            ensureAgentRunningResult = false,
            stopAgentResult = false,
        }).ConfigureAwait(false);

        using var response = await host.Client.PostAsync("/api/monitor/start", content: null).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.IsFalse(root.GetProperty("success").GetBoolean());
        Assert.AreEqual("Monitor startup failed: port bind failed", root.GetProperty("message").GetString());
        Assert.AreEqual("monitor-startup-failed", root.GetProperty("error").GetString());
        Assert.AreEqual("failed", root.GetProperty("startupState").GetString());
        Assert.AreEqual("port bind failed", root.GetProperty("startupFailureReason").GetString());
    }

    [TestMethod]
    public async Task MonitorStartEndpoint_ReturnsStarted_WhenLauncherTransitionsToRunningAsync()
    {
        using var host = await TestWebHost.StartAsync(new
        {
            statusSequence = new[]
            {
                CreateStatus(false, 5000, false, "Monitor info file not found. Start Monitor to initialize it.", "agent-info-missing"),
                CreateStatus(true, 6444, true, "Healthy on port 6444.", error: null),
            },
            ensureAgentRunningResult = true,
            stopAgentResult = false,
        }).ConfigureAwait(false);

        using var response = await host.Client.PostAsync("/api/monitor/start", content: null).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.IsTrue(root.GetProperty("success").GetBoolean());
        Assert.AreEqual("Monitor started on port 6444.", root.GetProperty("message").GetString());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("startupState").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("startupFailureReason").ValueKind);
    }

    [TestMethod]
    public async Task MonitorStartEndpoint_ReturnsStartingState_WhenLauncherHasNotReachedHealthyStateYetAsync()
    {
        using var host = await TestWebHost.StartAsync(new
        {
            statusSequence = new[]
            {
                CreateStatus(false, 5000, false, "Monitor info file not found. Start Monitor to initialize it.", "agent-info-missing"),
                CreateStatus(false, 5000, true, "Monitor is starting.", "monitor-starting"),
            },
            ensureAgentRunningResult = true,
            stopAgentResult = false,
        }).ConfigureAwait(false);

        using var response = await host.Client.PostAsync("/api/monitor/start", content: null).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.IsFalse(root.GetProperty("success").GetBoolean());
        Assert.AreEqual("Monitor is starting.", root.GetProperty("message").GetString());
        Assert.AreEqual("monitor-starting", root.GetProperty("error").GetString());
        Assert.AreEqual("starting", root.GetProperty("startupState").GetString());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("startupFailureReason").ValueKind);
    }

    [TestMethod]
    public async Task MonitorStopEndpoint_ReturnsAlreadyStopped_WhenLauncherReportsMissingMetadataAsync()
    {
        using var host = await TestWebHost.StartAsync(new
        {
            statusSequence = new[]
            {
                CreateStatus(false, 5000, false, "Monitor info file not found. Start Monitor to initialize it.", "agent-info-missing"),
            },
            ensureAgentRunningResult = false,
            stopAgentResult = false,
        }).ConfigureAwait(false);

        using var response = await host.Client.PostAsync("/api/monitor/stop", content: null).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.IsTrue(root.GetProperty("success").GetBoolean());
        Assert.AreEqual("Monitor already stopped (info file missing).", root.GetProperty("message").GetString());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("startupState").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("startupFailureReason").ValueKind);
    }

    [TestMethod]
    public async Task MonitorStopEndpoint_ReturnsFailure_WhenLauncherCannotStopMonitorAsync()
    {
        using var host = await TestWebHost.StartAsync(new
        {
            statusSequence = new[]
            {
                CreateStatus(true, 6555, true, "Healthy on port 6555.", error: null),
            },
            ensureAgentRunningResult = false,
            stopAgentResult = false,
        }).ConfigureAwait(false);

        using var response = await host.Client.PostAsync("/api/monitor/stop", content: null).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.IsFalse(root.GetProperty("success").GetBoolean());
        Assert.AreEqual("Failed to stop monitor.", root.GetProperty("message").GetString());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("startupState").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("startupFailureReason").ValueKind);
    }

    [TestMethod]
    public async Task MonitorStopEndpoint_ReturnsStopped_WhenLauncherStopsRunningMonitorAsync()
    {
        using var host = await TestWebHost.StartAsync(new
        {
            statusSequence = new[]
            {
                CreateStatus(true, 6333, true, "Healthy on port 6333.", error: null),
            },
            ensureAgentRunningResult = false,
            stopAgentResult = true,
        }).ConfigureAwait(false);

        using var response = await host.Client.PostAsync("/api/monitor/stop", content: null).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.IsTrue(root.GetProperty("success").GetBoolean());
        Assert.AreEqual("Monitor stopped on port 6333.", root.GetProperty("message").GetString());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("startupState").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("startupFailureReason").ValueKind);
    }

    private static object CreateStatus(bool isRunning, int port, bool hasMetadata, string message, string? error)
    {
        return new
        {
            isRunning,
            port,
            hasMetadata,
            message,
            error,
        };
    }
}
