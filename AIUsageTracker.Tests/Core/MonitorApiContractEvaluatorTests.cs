// <copyright file="MonitorApiContractEvaluatorTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Tests.Core;

public class MonitorApiContractEvaluatorTests
{
    [Fact]
    public void Evaluate_ReturnsCompatible_WhenMajorMatches()
    {
        var result = MonitorApiContractEvaluator.Evaluate("1.5", minClientContractVersion: null, "2.0.0", "1");

        Assert.True(result.IsReachable);
        Assert.True(result.IsCompatible);
        Assert.Equal("1.5", result.AgentContractVersion);
    }

    [Fact]
    public void Evaluate_ReturnsIncompatible_WhenMinClientVersionHigherThanClient()
    {
        var result = MonitorApiContractEvaluator.Evaluate("1.0", "1.2", "2.0.0", "1.1");

        Assert.True(result.IsReachable);
        Assert.False(result.IsCompatible);
        Assert.Contains("requires client contract", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_ReturnsIncompatible_WhenMajorMismatch()
    {
        var result = MonitorApiContractEvaluator.Evaluate("2.0", minClientContractVersion: null, "2.0.0", "1");

        Assert.True(result.IsReachable);
        Assert.False(result.IsCompatible);
        Assert.Contains("major mismatch", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
