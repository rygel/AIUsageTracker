// <copyright file="Index.cshtml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace AIUsageTracker.Web.Pages;

[OutputCache(PolicyName = "DashboardCache")]
public class IndexModel : PageModel
{
    private readonly WebDatabaseService _dbService;
    private readonly IUsageAnalyticsService _analyticsService;
    private readonly IPreferencesStore _preferencesStore;

    public IndexModel(WebDatabaseService dbService, IUsageAnalyticsService analyticsService, IPreferencesStore preferencesStore)
    {
        this._dbService = dbService;
        this._analyticsService = analyticsService;
        this._preferencesStore = preferencesStore;
    }

    public IReadOnlyList<ProviderUsage>? LatestUsage { get; set; }

    public UsageSummary? Summary { get; set; }

    public IReadOnlyDictionary<string, BurnRateForecast> ForecastsByProvider { get; private set; }
        = new Dictionary<string, BurnRateForecast>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ProviderReliabilitySnapshot> ReliabilityByProvider { get; private set; }
        = new Dictionary<string, ProviderReliabilitySnapshot>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, UsageAnomalySnapshot> AnomaliesByProvider { get; private set; }
        = new Dictionary<string, UsageAnomalySnapshot>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<BudgetStatus> BudgetStatuses { get; private set; } = [];

    public IReadOnlyList<UsageComparison> UsageComparisons { get; private set; } = [];

    public bool IsDatabaseAvailable => this._dbService.IsDatabaseAvailable();

    public bool ShowUsedPercentage { get; set; }

    public bool ShowInactiveProviders { get; set; }

    public bool EnableExperimentalAnomalyDetection { get; set; }

    // Always on.
    public bool EnableExperimentalBudgetPolicies { get; set; } = true;

    // Always on.
    public bool EnableExperimentalComparison { get; set; } = true;

    public int ColorThresholdYellow { get; set; } = 60;

    public int ColorThresholdRed { get; set; } = 80;

    public async Task OnGetAsync([FromQuery] bool? showUsed)
    {
        this.ResolveShowUsedPreference(showUsed);
        this.ResolveShowInactivePreference();
        this.ResolveExperimentalAnomalyPreference();
        await this.LoadColorThresholdsAsync().ConfigureAwait(false);

        // Budget and comparison are always enabled (experimental)
        this.EnableExperimentalBudgetPolicies = true;
        this.EnableExperimentalComparison = true;

        await this.LoadDashboardDataAsync().ConfigureAwait(false);
    }

    private void ResolveShowUsedPreference(bool? showUsed)
    {
        if (showUsed.HasValue)
        {
            this.ShowUsedPercentage = showUsed.Value;
            this.SetBooleanCookie("showUsedPercentage", this.ShowUsedPercentage);
        }
        else if (this.Request.Cookies.TryGetValue("showUsedPercentage", out var cookieValue) && bool.TryParse(cookieValue, out var cookiePref))
        {
            this.ShowUsedPercentage = cookiePref;
        }
        else
        {
            this.ShowUsedPercentage = false; // Default to showing remaining percentage.
        }
    }

    private void ResolveShowInactivePreference()
    {
        if (this.Request.Query.TryGetValue("showInactive", out var showInactiveQuery) &&
            bool.TryParse(showInactiveQuery, out var showInactive))
        {
            this.ShowInactiveProviders = showInactive;
            this.SetBooleanCookie("showInactiveProviders", this.ShowInactiveProviders);
        }
        else if (this.Request.Cookies.TryGetValue("showInactiveProviders", out var inactiveCookieValue) &&
                 bool.TryParse(inactiveCookieValue, out var inactiveCookiePref))
        {
            this.ShowInactiveProviders = inactiveCookiePref;
        }
        else
        {
            this.ShowInactiveProviders = false; // Default to hiding inactive providers.
        }
    }

    private void ResolveExperimentalAnomalyPreference()
    {
        if (this.Request.Query.TryGetValue("expAnomaly", out var expAnomalyQuery) &&
            bool.TryParse(expAnomalyQuery, out var expAnomaly))
        {
            this.EnableExperimentalAnomalyDetection = expAnomaly;
            this.SetBooleanCookie("expAnomaly", this.EnableExperimentalAnomalyDetection);
        }
        else if (this.Request.Cookies.TryGetValue("expAnomaly", out var expAnomalyCookie) &&
                 bool.TryParse(expAnomalyCookie, out var expAnomalyCookiePref))
        {
            this.EnableExperimentalAnomalyDetection = expAnomalyCookiePref;
        }
        else
        {
            this.EnableExperimentalAnomalyDetection = false;
        }
    }

    private async Task LoadDashboardDataAsync()
    {
        if (!this.IsDatabaseAvailable)
        {
            return;
        }

        var latestUsageTask = this._dbService.GetLatestUsageAsync(includeInactive: this.ShowInactiveProviders);
        var summaryTask = this._dbService.GetUsageSummaryAsync();

        await Task.WhenAll(latestUsageTask, summaryTask).ConfigureAwait(false);

        this.LatestUsage = await latestUsageTask.ConfigureAwait(false);
        this.Summary = await summaryTask.ConfigureAwait(false);

        if (this.LatestUsage.Count == 0)
        {
            return;
        }

        await this.LoadAnalyticsAsync(this.LatestUsage.Select(x => x.ProviderId).ToList()).ConfigureAwait(false);
    }

    private async Task LoadAnalyticsAsync(IReadOnlyList<string> providerIds)
    {
        var forecastTask = this._analyticsService.GetBurnRateForecastsAsync(providerIds);
        var reliabilityTask = this._analyticsService.GetProviderReliabilityAsync(providerIds);
        Task<IReadOnlyDictionary<string, UsageAnomalySnapshot>>? anomalyTask = null;
        if (this.EnableExperimentalAnomalyDetection)
        {
            anomalyTask = this._analyticsService.GetUsageAnomaliesAsync(providerIds);
            await Task.WhenAll(forecastTask, reliabilityTask, anomalyTask).ConfigureAwait(false);
        }
        else
        {
            await Task.WhenAll(forecastTask, reliabilityTask).ConfigureAwait(false);
        }

        this.ForecastsByProvider = await forecastTask.ConfigureAwait(false);
        this.ReliabilityByProvider = await reliabilityTask.ConfigureAwait(false);
        var anomalies = anomalyTask != null ? (await anomalyTask.ConfigureAwait(false)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase) : null;
        this.AnomaliesByProvider = anomalies ?? new Dictionary<string, UsageAnomalySnapshot>(StringComparer.OrdinalIgnoreCase);

        if (this.EnableExperimentalBudgetPolicies)
        {
            this.BudgetStatuses = (await this._analyticsService.GetBudgetStatusesAsync(providerIds).ConfigureAwait(false)).ToList();
        }

        if (this.EnableExperimentalComparison)
        {
            this.UsageComparisons = (await this._analyticsService.GetUsageComparisonsAsync(providerIds).ConfigureAwait(false)).ToList();
        }
    }

    private async Task LoadColorThresholdsAsync()
    {
        var prefs = await this._preferencesStore.LoadAsync().ConfigureAwait(false);
        this.ColorThresholdYellow = prefs.ColorThresholdYellow;
        this.ColorThresholdRed = prefs.ColorThresholdRed;
    }

    private void SetBooleanCookie(string name, bool value)
    {
        this.Response.Cookies.Append(name, value.ToString(), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
        });
    }
}
