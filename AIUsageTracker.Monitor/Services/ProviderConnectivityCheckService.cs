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
            return (false, "No usage data returned", 404);
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
