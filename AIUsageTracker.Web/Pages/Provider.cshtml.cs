// <copyright file="Provider.cshtml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Web.Pages
{
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Web.Services;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.RazorPages;

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

        public IReadOnlyList<ProviderUsage>? ProviderHistory { get; set; }

        public IReadOnlyList<ResetEvent>? ResetEvents { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (string.IsNullOrWhiteSpace(this.Id) && !string.IsNullOrWhiteSpace(this.ProviderIdQuery))
            {
                this.Id = this.ProviderIdQuery;
            }

            if (string.IsNullOrEmpty(this.Id))
            {
                return this.RedirectToPage("/Providers");
            }

            this.ProviderHistory = await this._dbService.GetProviderHistoryAsync(this.Id, 100).ConfigureAwait(false);

            if (this.ProviderHistory?.Any() != true)
            {
                return this.Page();
            }

            this.ProviderName = this.ProviderHistory.First().ProviderName;
            this.ResetEvents = await this._dbService.GetResetEventsAsync(this.Id, 50).ConfigureAwait(false);

            return this.Page();
        }
    }
}
