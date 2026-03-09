// <copyright file="ProviderRefreshCircuitBreakerServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class ProviderRefreshCircuitBreakerServiceTests
{
    private readonly ProviderRefreshCircuitBreakerService _service;

    public ProviderRefreshCircuitBreakerServiceTests()
    {
        var logger = new Mock<ILogger<ProviderRefreshCircuitBreakerService>>();
        this._service = new ProviderRefreshCircuitBreakerService(logger.Object);
    }

    [Fact]
    public void GetRefreshableConfigs_ReturnsAllProviders_WhenNoFailuresRecorded()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
            new() { ProviderId = "anthropic" },
        };

        var result = this._service.GetRefreshableConfigs(configs, forceAll: false);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, config => config.ProviderId == "openai");
        Assert.Contains(result, config => config.ProviderId == "anthropic");
    }

    [Fact]
    public void UpdateProviderFailureStates_OpensCircuitAfterThreeFailures()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            this._service.UpdateProviderFailureStates(configs, Array.Empty<ProviderUsage>());
        }

        var result = this._service.GetRefreshableConfigs(configs, forceAll: false);

        Assert.Empty(result);
    }

    [Fact]
    public void UpdateProviderFailureStates_ResetsCircuitAfterSuccessfulUsage()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            this._service.UpdateProviderFailureStates(configs, Array.Empty<ProviderUsage>());
        }

        this._service.UpdateProviderFailureStates(
            configs,
            new[]
            {
                new ProviderUsage
                {
                    ProviderId = "openai",
                    IsAvailable = true,
                    HttpStatus = 200,
                },
            });

        var result = this._service.GetRefreshableConfigs(configs, forceAll: false);

        Assert.Single(result);
        Assert.Equal("openai", result[0].ProviderId);
    }

    [Fact]
    public void GetRefreshableConfigs_ForceAllBypassesOpenCircuit()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai" },
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            this._service.UpdateProviderFailureStates(configs, Array.Empty<ProviderUsage>());
        }

        var result = this._service.GetRefreshableConfigs(configs, forceAll: true);

        Assert.Single(result);
        Assert.Equal("openai", result[0].ProviderId);
    }
}
