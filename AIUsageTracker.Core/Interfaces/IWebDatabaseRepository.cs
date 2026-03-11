// <copyright file="IWebDatabaseRepository.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.Interfaces;

public interface IWebDatabaseRepository
{
    Task<IReadOnlyList<ProviderUsage>> GetHistorySamplesAsync(IEnumerable<string> providerIds, int lookbackHours, int maxSamples);

    Task<IReadOnlyList<ProviderUsage>> GetAllHistoryForExportAsync(int limit = 0);
}
