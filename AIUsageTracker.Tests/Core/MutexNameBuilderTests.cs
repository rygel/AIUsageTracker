// <copyright file="MutexNameBuilderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Runtime;

namespace AIUsageTracker.Tests.Core;

public class MutexNameBuilderTests
{
    [Fact]
    public void BuildLocalName_SanitizesUserAndFormatsMutexName()
    {
        var result = MutexNameBuilder.BuildLocalName(
            "AIUsageTracker_SlimUI_",
            "Alex Smith/Dev\\Ops");

        Assert.Equal(@"Local\AIUsageTracker_SlimUI_Alex_Smith_Dev_Ops", result);
    }

    [Fact]
    public void BuildGlobalName_SanitizesUserAndFormatsMutexName()
    {
        var result = MutexNameBuilder.BuildGlobalName(
            "AIUsageTracker_MonitorLaunch_",
            "John Doe");

        Assert.Equal(@"Global\AIUsageTracker_MonitorLaunch_John_Doe", result);
    }

    [Fact]
    public void BuildLocalName_WithEmptyPrefix_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => MutexNameBuilder.BuildLocalName(string.Empty, "alex"));
    }
}
