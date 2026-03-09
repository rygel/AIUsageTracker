// <copyright file="IProviderService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Interfaces
{
    using AIUsageTracker.Core.Models;

    public interface IProviderService
    {
        string ProviderId { get; }

        ProviderDefinition Definition { get; }

        bool CanHandleProviderId(string providerId)
        {
            return this.Definition.HandlesProviderId(providerId);
        }

        Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null);
    }



}
