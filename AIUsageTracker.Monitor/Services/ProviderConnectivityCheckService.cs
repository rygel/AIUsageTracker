// <copyright file="ProviderConnectivityCheckService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Configuration;

namespace AIUsageTracker.Monitor.Services;

internal sealed class ProviderConnectivityCheckService
{
    private readonly IConfigService _configService;
    private readonly IProviderUsageProcessingPipeline _usageProcessingPipeline;

    public ProviderConnectivityCheckService(
        IConfigService configService,
        IProviderUsageProcessingPipeline usageProcessingPipeline)
    {
        this._configService = configService;
        this._usageProcessingPipeline = usageProcessingPipeline;
    }

    public async Task<(bool Success, string Message, int Status)> EvaluateAsync(
        string providerId,
        IEnumerable<ProviderUsage> usages)
    {
        var preferences = await this._configService.GetPreferencesAsync().ConfigureAwait(false);
        var processingResult = this._usageProcessingPipeline.Process(
            usages,
            new[] { providerId },
            preferences.IsPrivacyMode);
        var usage = processingResult.Usages.FirstOrDefault();

        if (usage == null)
        {
            // This only happens when the provider returned truly empty data (no description,
            // no quota values) which the pipeline correctly treats as a placeholder.
            var reason = processingResult.PlaceholderFilteredCount > 0
                ? "Provider returned no data — check authentication or API key configuration"
                : "Provider returned no usage data";
            return (false, reason, 503);
        }

        if (usage.HttpStatus >= 400 && usage.HttpStatus != 429)
        {
            return (false, usage.Description, usage.HttpStatus);
        }

        if (!usage.IsAvailable)
        {
            return (false, usage.Description, 503);
        }

        return (true, "Connected", 200);
    }
}
