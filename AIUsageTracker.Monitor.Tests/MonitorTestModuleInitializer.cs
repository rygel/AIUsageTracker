// <copyright file="MonitorTestModuleInitializer.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Runtime.CompilerServices;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Monitor.Tests;

internal static class MonitorTestModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        ProviderMetadataCatalog.Initialize(typeof(GroqProvider).Assembly);
    }
}
