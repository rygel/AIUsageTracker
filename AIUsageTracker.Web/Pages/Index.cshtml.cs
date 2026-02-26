using AIUsageTracker.Core.Models;
using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIUsageTracker.Web.Pages;

[OutputCache(PolicyName = "DashboardCache")]
public class IndexModel : PageModel
{
    private readonly WebDatabaseService _dbService;

    public IndexModel(WebDatabaseService dbService)
    {
        _dbService = dbService;
    }

    public List<ProviderUsage>? LatestUsage { get; set; }
    public UsageSummary? Summary { get; set; }
    public IReadOnlyDictionary<string, BurnRateForecast> ForecastsByProvider { get; private set; }
        = new Dictionary<string, BurnRateForecast>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, ProviderReliabilitySnapshot> ReliabilityByProvider { get; private set; }
        = new Dictionary<string, ProviderReliabilitySnapshot>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, UsageAnomalySnapshot> AnomaliesByProvider { get; private set; }
        = new Dictionary<string, UsageAnomalySnapshot>(StringComparer.OrdinalIgnoreCase);
    public bool IsDatabaseAvailable => _dbService.IsDatabaseAvailable();
    public bool ShowUsedPercentage { get; set; }
    public bool ShowInactiveProviders { get; set; }
    public bool EnableExperimentalAnomalyDetection { get; set; }

    public async Task OnGetAsync([FromQuery] bool? showUsed)
    {
        // Check query string first, then cookie, then default to false (show remaining)
        if (showUsed.HasValue)
        {
            ShowUsedPercentage = showUsed.Value;
            // Save preference to cookie
            Response.Cookies.Append("showUsedPercentage", ShowUsedPercentage.ToString(), new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = false,
                SameSite = SameSiteMode.Strict
            });
        }
        else if (Request.Cookies.TryGetValue("showUsedPercentage", out var cookieValue) && bool.TryParse(cookieValue, out var cookiePref))
        {
            ShowUsedPercentage = cookiePref;
        }
        else
        {
            ShowUsedPercentage = false; // Default to showing remaining percentage
        }

        if (Request.Query.TryGetValue("showInactive", out var showInactiveQuery) &&
            bool.TryParse(showInactiveQuery, out var showInactive))
        {
            ShowInactiveProviders = showInactive;
            Response.Cookies.Append("showInactiveProviders", ShowInactiveProviders.ToString(), new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = false,
                SameSite = SameSiteMode.Strict
            });
        }
        else if (Request.Cookies.TryGetValue("showInactiveProviders", out var inactiveCookieValue) &&
                 bool.TryParse(inactiveCookieValue, out var inactiveCookiePref))
        {
            ShowInactiveProviders = inactiveCookiePref;
        }
        else
        {
            ShowInactiveProviders = false; // Default to hiding inactive providers
        }

        if (Request.Query.TryGetValue("expAnomaly", out var expAnomalyQuery) &&
            bool.TryParse(expAnomalyQuery, out var expAnomaly))
        {
            EnableExperimentalAnomalyDetection = expAnomaly;
            Response.Cookies.Append("expAnomaly", EnableExperimentalAnomalyDetection.ToString(), new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = false,
                SameSite = SameSiteMode.Strict
            });
        }
        else if (Request.Cookies.TryGetValue("expAnomaly", out var expAnomalyCookie) &&
                 bool.TryParse(expAnomalyCookie, out var expAnomalyCookiePref))
        {
            EnableExperimentalAnomalyDetection = expAnomalyCookiePref;
        }
        else
        {
            EnableExperimentalAnomalyDetection = false;
        }

        if (IsDatabaseAvailable)
        {
            var latestUsageTask = _dbService.GetLatestUsageAsync(includeInactive: ShowInactiveProviders);
            var summaryTask = _dbService.GetUsageSummaryAsync();

            await Task.WhenAll(latestUsageTask, summaryTask);

            LatestUsage = latestUsageTask.Result;
            Summary = summaryTask.Result;

            if (LatestUsage.Count > 0)
            {
                var providerIds = LatestUsage.Select(x => x.ProviderId).ToList();
                var forecastTask = _dbService.GetBurnRateForecastsAsync(providerIds);
                var reliabilityTask = _dbService.GetProviderReliabilityAsync(providerIds);
                Task<Dictionary<string, UsageAnomalySnapshot>>? anomalyTask = null;
                if (EnableExperimentalAnomalyDetection)
                {
                    anomalyTask = _dbService.GetUsageAnomaliesAsync(providerIds);
                    await Task.WhenAll(forecastTask, reliabilityTask, anomalyTask);
                }
                else
                {
                    await Task.WhenAll(forecastTask, reliabilityTask);
                }

                ForecastsByProvider = forecastTask.Result;
                ReliabilityByProvider = reliabilityTask.Result;
                AnomaliesByProvider = anomalyTask?.Result
                    ?? new Dictionary<string, UsageAnomalySnapshot>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}

