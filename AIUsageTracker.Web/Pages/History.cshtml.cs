using AIUsageTracker.Core.Models;
using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIUsageTracker.Web.Pages;

public class HistoryModel : PageModel
{
    private readonly WebDatabaseService _dbService;

    public HistoryModel(WebDatabaseService dbService)
    {
        this._dbService = dbService;
    }

    public List<ProviderUsage>? History { get; set; }

    public bool IsDatabaseAvailable => this._dbService.IsDatabaseAvailable();

    public async Task OnGetAsync()
    {
        if (this.IsDatabaseAvailable)
        {
            this.History = await this._dbService.GetHistoryAsync(100).ConfigureAwait(false);
        }
    }
}

