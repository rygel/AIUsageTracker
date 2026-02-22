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
    public bool IsDatabaseAvailable => _dbService.IsDatabaseAvailable();
    public bool ShowUsedPercentage { get; set; }
    public bool ShowInactiveProviders { get; set; }

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

        if (IsDatabaseAvailable)
        {
            var latestUsageTask = _dbService.GetLatestUsageAsync(includeInactive: ShowInactiveProviders);
            var summaryTask = _dbService.GetUsageSummaryAsync();

            await Task.WhenAll(latestUsageTask, summaryTask);

            LatestUsage = latestUsageTask.Result;
            Summary = summaryTask.Result;
        }
    }
}

