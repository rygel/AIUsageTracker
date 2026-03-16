// <copyright file="IProviderUsageProcessingPipeline.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Monitor.Services;

public interface IProviderUsageProcessingPipeline
{
    ProviderUsageProcessingResult Process(
        IEnumerable<ProviderUsage> usages,
        IReadOnlyCollection<string> activeProviderIds,
        bool isPrivacyMode);

    ProviderUsageProcessingTelemetrySnapshot GetSnapshot();
}
