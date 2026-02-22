using AIUsageTracker.Core.Models;
using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIUsageTracker.Web.Pages;

public class HistoryModel : PageModel
{
    private readonly WebDatabaseService _dbService;

    public HistoryModel(WebDatabaseService dbService)
    {
        _dbService = dbService;
    }

    public List<ProviderUsage>? History { get; set; }
    public bool IsDatabaseAvailable => _dbService.IsDatabaseAvailable();

    public async Task OnGetAsync()
    {
        if (IsDatabaseAvailable)
        {
            History = await _dbService.GetHistoryAsync(100);
        }
    }
}

