// <copyright file="TestWebHost.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Tests.Infrastructure;

namespace AIUsageTracker.Web.Tests;

internal sealed class TestWebHost : IDisposable
{
    private const string ScenarioPathEnvironmentVariable = "AIUSAGETRACKER_WEB_TEST_MONITOR_SCENARIO";

    private readonly string _tempDirectory;
    private readonly KestrelWebApplicationFactory<Program> _factory;

    private TestWebHost(string tempDirectory, KestrelWebApplicationFactory<Program> factory, HttpClient client)
    {
        this._tempDirectory = tempDirectory;
        this._factory = factory;
        this.Client = client;
    }

    public HttpClient Client { get; }

    public static async Task<TestWebHost> StartAsync(object scenario)
    {
        var tempDirectory = TestTempPaths.CreateDirectory("aiusagetracker-webtests");

        var scenarioPath = Path.Combine(tempDirectory, "monitor-scenario.json");
        await File.WriteAllTextAsync(scenarioPath, JsonSerializer.Serialize(scenario)).ConfigureAwait(false);

        var factory = new KestrelWebApplicationFactory<Program>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ScenarioPathEnvironmentVariable] = scenarioPath,
        });
        var client = new HttpClient
        {
            BaseAddress = new Uri(factory.ServerAddress),
        };

        return new TestWebHost(tempDirectory, factory, client);
    }

    public void Dispose()
    {
        this.Client.Dispose();
        this._factory.Dispose();
        TestTempPaths.CleanupPath(this._tempDirectory);
    }
}
