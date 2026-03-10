// <copyright file="MonitorHealthSnapshotTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Tests.Core;

public class MonitorHealthSnapshotTests
{
    [Fact]
    public void EffectiveContractVersion_PrefersCanonicalField()
    {
        var snapshot = new MonitorHealthSnapshot
        {
            ContractVersion = "1.2",
            ApiContractVersion = "1.1",
        };

        Assert.Equal("1.2", snapshot.EffectiveContractVersion);
    }

    [Fact]
    public void EffectiveContractVersion_FallsBackToLegacyField()
    {
        var snapshot = new MonitorHealthSnapshot
        {
            ApiContractVersion = "1.1",
        };

        Assert.Equal("1.1", snapshot.EffectiveContractVersion);
    }

    [Fact]
    public void EffectiveMinClientContractVersion_PrefersCanonicalField()
    {
        var snapshot = new MonitorHealthSnapshot
        {
            MinClientContractVersion = "1.3",
            MinClientApiContractVersion = "1.2",
        };

        Assert.Equal("1.3", snapshot.EffectiveMinClientContractVersion);
    }

    [Fact]
    public void EffectiveMinClientContractVersion_FallsBackToLegacyField()
    {
        var snapshot = new MonitorHealthSnapshot
        {
            MinClientApiContractVersion = "1.2",
        };

        Assert.Equal("1.2", snapshot.EffectiveMinClientContractVersion);
    }
}
