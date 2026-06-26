// <copyright file="TestModuleInitializer.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Runtime.CompilerServices;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests;

internal static class TestModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        ProviderMetadataCatalog.Initialize(typeof(GroqProvider).Assembly);
    }
}
