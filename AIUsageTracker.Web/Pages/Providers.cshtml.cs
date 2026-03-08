using AIUsageTracker.Web.Services;
using AIUsageTracker.Core.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIUsageTracker.Web.Pages;

public class ProvidersModel : PageModel
{
    private readonly WebDatabaseService _dbService;

    public ProvidersModel(WebDatabaseService dbService)
    {
        this._dbService = dbService;
    }

    public List<ProviderInfo>? Providers { get; set; }

    public bool IsDatabaseAvailable => this._dbService.IsDatabaseAvailable();

    public async Task OnGetAsync()
    {
        if (this.IsDatabaseAvailable)
        {
            this.Providers = await this._dbService.GetProvidersAsync().ConfigureAwait(false);
        }
    }
}

