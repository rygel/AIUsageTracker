// <copyright file="AntigravityProviderParsingTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Tests.Infrastructure;

public sealed class AntigravityProviderParsingTests
{
    [Theory]
    [InlineData("--csrf-token abc123def", "abc123def")]
    [InlineData("--csrf_token xyz789", "xyz789")]
    [InlineData("--csrf-token=myToken123", "myToken123")]
    [InlineData("--csrf_token=another-token-here", "another-token-here")]
    [InlineData("--other-flag --csrf-token testValue", "testValue")]
    [InlineData("no-csrf-token-here", null)]
    [InlineData("", null)]
    public void ParseCsrfToken_ExtractsCorrectly(string commandLine, string? expected)
    {
        var result = InvokeStaticMethod<string?>("ParseCsrfToken", commandLine);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("--extension_server_port 8080", 8080)]
    [InlineData("--extension_server_port=9090", 9090)]
    [InlineData("--extension_server_port=12345 some-args", 12345)]
    [InlineData("no-port-here", null)]
    [InlineData("", null)]
    public void ParseExtensionServerPort_ExtractsCorrectly(string commandLine, int? expected)
    {
        var result = InvokeStaticMethod<int?>("ParseExtensionServerPort", commandLine);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveResetInfo_ReturnsEmpty_WhenNoResetTime()
    {
        var (description, nextResetTime) = InvokeResolveResetInfo(null);
        Assert.Equal(string.Empty, description);
        Assert.Null(nextResetTime);
    }

    [Fact]
    public void ResolveResetInfo_ReturnsEmpty_WhenResetTimeEmpty()
    {
        var (description, nextResetTime) = InvokeResolveResetInfo("");
        Assert.Equal(string.Empty, description);
        Assert.Null(nextResetTime);
    }

    [Fact]
    public void ResolveResetInfo_ReturnsEmpty_WhenResetTimePast()
    {
        var (description, nextResetTime) = InvokeResolveResetInfo("2020-01-01T00:00:00Z");
        Assert.Equal(string.Empty, description);
        Assert.Null(nextResetTime);
    }

    [Fact]
    public void ResolveResetInfo_ReturnsResetInfo_WhenResetTimeFuture()
    {
        var futureDate = DateTime.UtcNow.AddHours(5).ToString("o");
        var (description, nextResetTime) = InvokeResolveResetInfo(futureDate);

        Assert.NotEmpty(description);
        Assert.NotNull(nextResetTime);
        Assert.Contains("Resets:", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsNotRunning_WhenNoProcessesAndNoCache()
    {
        var provider = new AntigravityProvider(
            new HttpClient(),
            Mock.Of<ILogger<AntigravityProvider>>());

        var config = new ProviderConfig
        {
            ProviderId = "antigravity",
        };

        var result = await provider.GetUsageAsync(config);
        var list = result.ToList();

        Assert.Single(list);
        Assert.True(list[0].IsAvailable);
        Assert.Equal("antigravity", list[0].ProviderId);
    }

    [Fact]
    public async Task GetUsageAsync_ThrowsOnNullConfig()
    {
        var provider = new AntigravityProvider(
            new HttpClient(),
            Mock.Of<ILogger<AntigravityProvider>>());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.GetUsageAsync(null!));
    }

    [Fact]
    public void StaticDefinition_HasExpectedProperties()
    {
        var def = AntigravityProvider.StaticDefinition;

        Assert.Equal("antigravity", def.ProviderId);
        Assert.Equal("Google Antigravity", def.DisplayName);
        Assert.True(def.IsQuotaBased);
        Assert.Equal(PlanType.Coding, def.PlanType);
        Assert.True(def.RefreshOnStartupWithCachedData);
        Assert.True(def.SupportsAccountIdentity);
    }

    private static T? InvokeStaticMethod<T>(string methodName, params object[] args)
    {
        var method = typeof(AntigravityProvider)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (T?)method!.Invoke(null, args);
    }

    private static (string Description, DateTime? NextResetTime) InvokeResolveResetInfo(string? resetTime)
    {
        var configType = typeof(AntigravityProvider).GetNestedType("ClientModelConfig", BindingFlags.NonPublic);
        var quotaType = typeof(AntigravityProvider).GetNestedType("QuotaInfo", BindingFlags.NonPublic);

        object? modelConfig = null;
        if (resetTime != null)
        {
            var quota = Activator.CreateInstance(quotaType!)!;
            quotaType!.GetProperty("ResetTime")!.SetValue(quota, resetTime);
            modelConfig = Activator.CreateInstance(configType!)!;
            configType!.GetProperty("QuotaInfo")!.SetValue(modelConfig, quota);
        }

        var method = typeof(AntigravityProvider)
            .GetMethod("ResolveResetInfo", BindingFlags.NonPublic | BindingFlags.Static)!;

        return ((string, DateTime?))method.Invoke(null, new object?[] { modelConfig })!;
    }
}
