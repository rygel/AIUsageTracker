// <copyright file="History.cshtml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Web.Pages
{
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Web.Services;
    using Microsoft.AspNetCore.Mvc.RazorPages;

    public class HistoryModel : PageModel
    {
        private readonly WebDatabaseService _dbService;

        public HistoryModel(WebDatabaseService dbService)
        {
            this._dbService = dbService;
        }

        public IReadOnlyList<ProviderUsage>? History { get; set; }

        public bool IsDatabaseAvailable => this._dbService.IsDatabaseAvailable();

        public async Task OnGetAsync()
        {
            if (this.IsDatabaseAvailable)
            {
                this.History = await this._dbService.GetHistoryAsync(100).ConfigureAwait(false);
            }
        }
    }
}
