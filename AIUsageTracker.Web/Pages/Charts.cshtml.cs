// <copyright file="Charts.cshtml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Memory;

namespace AIUsageTracker.Web.Pages;

[OutputCache(PolicyName = "ChartsCache")]
public class ChartsModel : PageModel
{
    private const string ProviderColorsCacheKey = "charts-provider-colors-v1";

    private readonly AIUsageTracker.Core.Interfaces.IConfigLoader _configLoader;
    private readonly WebDatabaseService _dbService;
    private readonly IMemoryCache _memoryCache;

    public ChartsModel(
        WebDatabaseService dbService,
        AIUsageTracker.Core.Interfaces.IConfigLoader configLoader,
        IMemoryCache memoryCache)
    {
        this._dbService = dbService;
        this._configLoader = configLoader;
        this._memoryCache = memoryCache;
    }

    public IReadOnlyList<ChartDataPoint>? ChartData { get; set; }

    public IReadOnlyDictionary<string, string> ProviderColors { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public bool IsDatabaseAvailable => this._dbService.IsDatabaseAvailable();

    public async Task OnGetAsync(int hours = 24)
    {
        if (this.IsDatabaseAvailable)
        {
            var chartTask = this._dbService.GetChartDataAsync(hours);
            var colorTask = this.GetProviderColorsAsync();

            await Task.WhenAll(chartTask, colorTask).ConfigureAwait(false);

            this.ChartData = await chartTask.ConfigureAwait(false);
            this.ProviderColors = await colorTask.ConfigureAwait(false);
        }
    }

    public async Task<IActionResult> OnGetResetEventsAsync(int hours = 24)
    {
        if (!this.IsDatabaseAvailable)
        {
            return new JsonResult(Array.Empty<ResetEvent>());
        }

        var events = await this._dbService.GetRecentResetEventsAsync(hours).ConfigureAwait(false);
        return new JsonResult(events);
    }

    public async Task<IActionResult> OnGetChartPayloadAsync(int hours = 24)
    {
        if (!this.IsDatabaseAvailable)
        {
            return new JsonResult(new
            {
                chartData = Array.Empty<ChartDataPoint>(),
                resetEvents = Array.Empty<ResetEvent>(),
            });
        }

        var chartTask = this._dbService.GetChartDataAsync(hours);
        var resetTask = this._dbService.GetRecentResetEventsAsync(hours);
        await Task.WhenAll(chartTask, resetTask).ConfigureAwait(false);

        return new JsonResult(new
        {
            chartData = await chartTask.ConfigureAwait(false),
            resetEvents = await resetTask.ConfigureAwait(false),
        });
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
