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
        this._dbService = dbService;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true, Name = "providerId")]
    public string? ProviderIdQuery { get; set; }

    public string? ProviderName { get; set; }

    public List<ProviderUsage>? ProviderHistory { get; set; }

    public List<ResetEvent>? ResetEvents { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(this.Id) && !string.IsNullOrWhiteSpace(this.ProviderIdQuery))
        {
            this.Id = this.ProviderIdQuery;
        }

        if (string.IsNullOrEmpty(this.Id))
        {
            return RedirectToPage("/Providers");
        }

        this.ProviderHistory = await this._dbService.GetProviderHistoryAsync(this.Id, 100).ConfigureAwait(false);

        if (this.ProviderHistory?.Any() != true)
        {
            return Page();
        }

        this.ProviderName = this.ProviderHistory.First().ProviderName;
        this.ResetEvents = await this._dbService.GetResetEventsAsync(this.Id, 50).ConfigureAwait(false);

        return Page();
    }
}

