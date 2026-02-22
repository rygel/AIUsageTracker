using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIUsageTracker.Web.Pages;

public class ProvidersModel : PageModel
{
    private readonly WebDatabaseService _dbService;

    public ProvidersModel(WebDatabaseService dbService)
    {
        _dbService = dbService;
    }

    public List<ProviderInfo>? Providers { get; set; }
    public bool IsDatabaseAvailable => _dbService.IsDatabaseAvailable();

    public async Task OnGetAsync()
    {
        if (IsDatabaseAvailable)
        {
            Providers = await _dbService.GetProvidersAsync();
        }
    }
}

