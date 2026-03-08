using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIUsageTracker.Web.Pages;

[OutputCache(PolicyName = "DashboardCache")]
public class IndexModel : PageModel
{
    private readonly WebDatabaseService _dbService;
    private readonly IUsageAnalyticsService _analyticsService;

    public IndexModel(WebDatabaseService dbService, IUsageAnalyticsService analyticsService)
    {
        this._dbService = dbService;
        this._analyticsService = analyticsService;
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

    public async Task OnGetAsync([FromQuery] bool? showUsed)
    {
        // Check query string first, then cookie, then default to false (show remaining)
        if (showUsed.HasValue)
        {
            this.ShowUsedPercentage = showUsed.Value;

            // Save preference to cookie.
            this.Response.Cookies.Append("showUsedPercentage", this.ShowUsedPercentage.ToString(), new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = false,
                SameSite = SameSiteMode.Strict,
            });
        }
        else if (this.Request.Cookies.TryGetValue("showUsedPercentage", out var cookieValue) && bool.TryParse(cookieValue, out var cookiePref))
        {
            this.ShowUsedPercentage = cookiePref;
        }
        else
        {
            this.ShowUsedPercentage = false; // Default to showing remaining percentage.
        }

        if (this.Request.Query.TryGetValue("showInactive", out var showInactiveQuery) &&
            bool.TryParse(showInactiveQuery, out var showInactive))
        {
            this.ShowInactiveProviders = showInactive;
            this.Response.Cookies.Append("showInactiveProviders", this.ShowInactiveProviders.ToString(), new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = false,
                SameSite = SameSiteMode.Strict,
            });
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

        if (this.Request.Query.TryGetValue("expAnomaly", out var expAnomalyQuery) &&
            bool.TryParse(expAnomalyQuery, out var expAnomaly))
        {
            this.EnableExperimentalAnomalyDetection = expAnomaly;
            this.Response.Cookies.Append("expAnomaly", this.EnableExperimentalAnomalyDetection.ToString(), new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = false,
                SameSite = SameSiteMode.Strict,
            });
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

        // Budget and comparison are always enabled (experimental)
        this.EnableExperimentalBudgetPolicies = true;
        this.EnableExperimentalComparison = true;

        if (this.IsDatabaseAvailable)
        {
            var latestUsageTask = this._dbService.GetLatestUsageAsync(includeInactive: this.ShowInactiveProviders);
            var summaryTask = this._dbService.GetUsageSummaryAsync();

            await Task.WhenAll(latestUsageTask, summaryTask);

            this.LatestUsage = await latestUsageTask;
            this.Summary = await summaryTask;

            if (this.LatestUsage.Count > 0)
            {
                var providerIds = this.LatestUsage.Select(x => x.ProviderId).ToList();
                var forecastTask = this._analyticsService.GetBurnRateForecastsAsync(providerIds);
                var reliabilityTask = this._analyticsService.GetProviderReliabilityAsync(providerIds);
                Task<IReadOnlyDictionary<string, UsageAnomalySnapshot>>? anomalyTask = null;
                if (this.EnableExperimentalAnomalyDetection)
                {
                    anomalyTask = this._analyticsService.GetUsageAnomaliesAsync(providerIds);
                    await Task.WhenAll(forecastTask, reliabilityTask, anomalyTask);
                }
                else
                {
                    await Task.WhenAll(forecastTask, reliabilityTask);
                }

                this.ForecastsByProvider = await forecastTask;
                this.ReliabilityByProvider = await reliabilityTask;
                var anomalies = anomalyTask != null ? (await anomalyTask).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase) : null;
                this.AnomaliesByProvider = anomalies ?? new Dictionary<string, UsageAnomalySnapshot>(StringComparer.OrdinalIgnoreCase);

                // Load experimental features
                if (this.EnableExperimentalBudgetPolicies)
                {
                    this.BudgetStatuses = (await this._analyticsService.GetBudgetStatusesAsync(providerIds)).ToList();
                }

                if (this.EnableExperimentalComparison)
                {
                    this.UsageComparisons = (await this._analyticsService.GetUsageComparisonsAsync(providerIds)).ToList();
                }
            }
        }
    }
}
