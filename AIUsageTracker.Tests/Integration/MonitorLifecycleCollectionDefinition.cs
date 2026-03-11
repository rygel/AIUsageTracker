// <copyright file="MonitorLifecycleCollectionDefinition.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Core;

// These tests manipulate a shared Monitor process and must not run in parallel.
[CollectionDefinition("MonitorLifecycle", DisableParallelization = true)]
public sealed class MonitorLifecycleCollectionDefinition
{
}
