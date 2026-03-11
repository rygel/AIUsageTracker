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
#pragma warning disable CS0618
        var snapshot = new MonitorHealthSnapshot
        {
            ContractVersion = "1.2",
            ApiContractVersion = "1.1",
        };
#pragma warning restore CS0618

        Assert.Equal("1.2", snapshot.EffectiveContractVersion);
    }

    [Fact]
    public void EffectiveContractVersion_FallsBackToLegacyField()
    {
#pragma warning disable CS0618
        var snapshot = new MonitorHealthSnapshot
        {
            ApiContractVersion = "1.1",
        };
#pragma warning restore CS0618

        Assert.Equal("1.1", snapshot.EffectiveContractVersion);
    }

    [Fact]
    public void EffectiveMinClientContractVersion_PrefersCanonicalField()
    {
#pragma warning disable CS0618
        var snapshot = new MonitorHealthSnapshot
        {
            MinClientContractVersion = "1.3",
            MinClientApiContractVersion = "1.2",
        };
#pragma warning restore CS0618

        Assert.Equal("1.3", snapshot.EffectiveMinClientContractVersion);
    }

    [Fact]
    public void EffectiveMinClientContractVersion_FallsBackToLegacyField()
    {
#pragma warning disable CS0618
        var snapshot = new MonitorHealthSnapshot
        {
            MinClientApiContractVersion = "1.2",
        };
#pragma warning restore CS0618

        Assert.Equal("1.2", snapshot.EffectiveMinClientContractVersion);
    }
}
