// <copyright file="Analytics.cshtml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Memory;

namespace AIUsageTracker.Web.Pages;

[OutputCache(PolicyName = "AnalyticsCache")]
public class AnalyticsModel : PageModel
{
    private const string ProviderColorsCacheKey = "analytics-provider-colors-v1";

    private readonly WebDatabaseService _dbService;
    private readonly IConfigLoader _configLoader;
    private readonly IMemoryCache _memoryCache;

    public AnalyticsModel(
        WebDatabaseService dbService,
        IConfigLoader configLoader,
        IMemoryCache memoryCache)
    {
        this._dbService = dbService;
        this._configLoader = configLoader;
        this._memoryCache = memoryCache;
    }

    public IReadOnlyList<SpendingTrendPoint>? SpendingData { get; set; }

    public IReadOnlyDictionary<string, string> ProviderColors { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public bool IsDatabaseAvailable => this._dbService.IsDatabaseAvailable();

    public bool HasCurrencyProviders { get; private set; }

    public async Task OnGetAsync(int days = 30)
    {
        if (!this.IsDatabaseAvailable)
        {
            return;
        }

        var currencyProviderIds = ProviderMetadataCatalog.Definitions
            .Where(d => d.IsCurrencyUsage)
            .Select(d => d.ProviderId)
            .ToList();

        this.HasCurrencyProviders = currencyProviderIds.Count > 0;

        var dataTask = this._dbService.GetSpendingTrendAsync(currencyProviderIds, days);
        var colorTask = this.GetProviderColorsAsync();

        await Task.WhenAll(dataTask, colorTask).ConfigureAwait(false);

        this.SpendingData = await dataTask.ConfigureAwait(false);
        this.ProviderColors = await colorTask.ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, string>> GetProviderColorsAsync()
    {
        return await this._memoryCache.GetOrCreateAsync(ProviderColorsCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var configs = await this._configLoader.LoadConfigAsync().ConfigureAwait(false);
            foreach (var model in configs
                .Where(cfg => cfg.Models != null)
                .SelectMany(cfg => cfg.Models!)
                .Where(m => !string.IsNullOrEmpty(m.Color) && !string.IsNullOrEmpty(m.Name)))
            {
                colors[model.Name!] = model.Color!;
            }

            return colors;
        }).ConfigureAwait(false) ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
