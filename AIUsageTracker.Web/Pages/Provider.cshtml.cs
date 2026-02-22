using AIUsageTracker.Core.Models;
using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIUsageTracker.Web.Pages;

public class ProviderModel : PageModel
{
    private readonly WebDatabaseService _dbService;

    public ProviderModel(WebDatabaseService dbService)
    {
        _dbService = dbService;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    public string? ProviderName { get; set; }
    public List<ProviderUsage>? ProviderHistory { get; set; }
    public List<ResetEvent>? ResetEvents { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(Id))
        {
            return RedirectToPage("/Providers");
        }

        ProviderHistory = await _dbService.GetProviderHistoryAsync(Id, 100);
        
        if (ProviderHistory?.Any() != true)
        {
            return Page();
        }

        ProviderName = ProviderHistory.First().ProviderName;
        ResetEvents = await _dbService.GetResetEventsAsync(Id, 50);

        return Page();
    }
}

