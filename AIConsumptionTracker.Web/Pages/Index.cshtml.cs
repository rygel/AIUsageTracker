using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIConsumptionTracker.Web.Pages;

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

        if (IsDatabaseAvailable)
        {
            LatestUsage = await _dbService.GetLatestUsageAsync();
            Summary = await _dbService.GetUsageSummaryAsync();
        }
    }
}
