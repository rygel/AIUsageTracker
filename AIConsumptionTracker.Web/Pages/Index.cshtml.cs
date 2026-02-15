using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Web.Services;
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

    public async Task OnGetAsync()
    {
        if (IsDatabaseAvailable)
        {
            LatestUsage = await _dbService.GetLatestUsageAsync();
            Summary = await _dbService.GetUsageSummaryAsync();
        }
    }
}
