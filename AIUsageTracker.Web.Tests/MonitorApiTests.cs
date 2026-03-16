// <copyright file="MonitorApiTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text.Json;

namespace AIUsageTracker.Web.Tests;

[TestClass]
[DoNotParallelize]
public class MonitorApiTests
{
    [TestMethod]
    public async Task MonitorStatusEndpoint_ReturnsExpectedContractAsync()
    {
        using var host = await TestWebHost.StartAsync(new
        {
            statusSequence = new[]
            {
                new
                {
                    isRunning = false,
                    port = 5000,
                    hasMetadata = false,
                    message = "Monitor info file not found. Start Monitor to initialize it.",
                    error = "agent-info-missing",
                },
            },
            ensureAgentRunningResult = false,
            stopAgentResult = false,
        }).ConfigureAwait(false);

        using var response = await host.Client.GetAsync("/api/monitor/status").ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.AreEqual(JsonValueKind.Object, root.ValueKind);
        Assert.IsTrue(root.TryGetProperty("isRunning", out var isRunning));
        Assert.IsTrue(isRunning.ValueKind is JsonValueKind.True or JsonValueKind.False);
        Assert.IsTrue(root.TryGetProperty("port", out var port));
        Assert.AreEqual(JsonValueKind.Number, port.ValueKind);
        Assert.IsTrue(root.TryGetProperty("message", out var message));
        Assert.AreEqual(JsonValueKind.String, message.ValueKind);
        Assert.IsTrue(root.TryGetProperty("error", out var error));
        Assert.IsTrue(error.ValueKind is JsonValueKind.String or JsonValueKind.Null);
        Assert.IsTrue(root.TryGetProperty("serviceHealth", out var serviceHealth));
        Assert.IsTrue(serviceHealth.ValueKind is JsonValueKind.String or JsonValueKind.Null);
        Assert.IsTrue(root.TryGetProperty("lastRefreshError", out var lastRefreshError));
        Assert.IsTrue(lastRefreshError.ValueKind is JsonValueKind.String or JsonValueKind.Null);
        Assert.IsTrue(root.TryGetProperty("providersInBackoff", out var providersInBackoff));
        Assert.AreEqual(JsonValueKind.Number, providersInBackoff.ValueKind);
        Assert.IsTrue(root.TryGetProperty("failingProviders", out var failingProviders));
        Assert.AreEqual(JsonValueKind.Array, failingProviders.ValueKind);
        Assert.IsTrue(root.TryGetProperty("startupState", out var startupState));
        Assert.IsTrue(startupState.ValueKind is JsonValueKind.String or JsonValueKind.Null);
        Assert.IsTrue(root.TryGetProperty("startupFailureReason", out var startupFailureReason));
        Assert.IsTrue(startupFailureReason.ValueKind is JsonValueKind.String or JsonValueKind.Null);
    }
}
