// <copyright file="Analytics.cshtml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace AIUsageTracker.Web.Pages;

[OutputCache(PolicyName = "AnalyticsCache")]
public class AnalyticsModel : PageModel
{
    private readonly WebDatabaseService _dbService;

    public AnalyticsModel(WebDatabaseService dbService)
    {
        this._dbService = dbService;
    }

    public IReadOnlyList<ModelUsageBreakdown> ModelBreakdown { get; private set; } = [];

    public IReadOnlyList<LatencyDataPoint> LatencyData { get; private set; } = [];

    public IReadOnlyList<HttpStatusHistoryPoint> HttpStatusData { get; private set; } = [];

    public IReadOnlyList<DetailsJsonEntry> DetailsEntries { get; private set; } = [];

    public bool IsDatabaseAvailable => this._dbService.IsDatabaseAvailable();

    public int SelectedHours { get; set; } = 24;

    public async Task OnGetAsync(int hours = 24)
    {
        this.SelectedHours = hours;

        if (!this.IsDatabaseAvailable)
        {
            return;
        }

        var modelTask = this._dbService.GetModelUsageBreakdownAsync(168);
        var latencyTask = this._dbService.GetLatencyTrendAsync(hours);
        var httpTask = this._dbService.GetHttpStatusHistoryAsync(hours);
        var detailsTask = this._dbService.GetRecentDetailsJsonAsync(20);

        await Task.WhenAll(modelTask, latencyTask, httpTask, detailsTask).ConfigureAwait(false);

        this.ModelBreakdown = await modelTask.ConfigureAwait(false);
        this.LatencyData = await latencyTask.ConfigureAwait(false);
        this.HttpStatusData = await httpTask.ConfigureAwait(false);
        this.DetailsEntries = await detailsTask.ConfigureAwait(false);
    }

    public async Task<IActionResult> OnGetAnalyticsPayloadAsync(int hours = 24)
    {
        if (!this.IsDatabaseAvailable)
        {
            return new JsonResult(new
            {
                latencyData = Array.Empty<LatencyDataPoint>(),
                httpStatusData = Array.Empty<HttpStatusHistoryPoint>(),
            });
        }

        var latencyTask = this._dbService.GetLatencyTrendAsync(hours);
        var httpTask = this._dbService.GetHttpStatusHistoryAsync(hours);
        await Task.WhenAll(latencyTask, httpTask).ConfigureAwait(false);

        return new JsonResult(new
        {
            latencyData = await latencyTask.ConfigureAwait(false),
            httpStatusData = await httpTask.ConfigureAwait(false),
        });
    }
}
