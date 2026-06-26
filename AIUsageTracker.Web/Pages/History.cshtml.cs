// <copyright file="History.cshtml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIUsageTracker.Web.Pages;

public class HistoryModel : PageModel
{
    private const int ExportRowLimit = 10_000;
    private const int ExportDayLimit = 90;

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

    public async Task<IActionResult> OnGetExportAsync()
    {
        if (!this.IsDatabaseAvailable)
        {
            return this.NotFound("Database not available.");
        }

        var data = await this._dbService.GetAllHistoryForExportAsync(ExportRowLimit).ConfigureAwait(false);

        var cutoff = DateTime.UtcNow.AddDays(-ExportDayLimit);
        var filtered = data
            .Where(u => u.FetchedAt >= cutoff)
            .OrderByDescending(u => u.FetchedAt)
            .ToList();

        var csv = new StringBuilder();
        csv.AppendLine("timestamp,provider_id,provider_name,requests_used,requests_available,requests_percentage,is_available");

        foreach (var usage in filtered)
        {
            csv.Append(CultureInfo.InvariantCulture, $"{usage.FetchedAt:O},");
            csv.Append(CsvEscape(usage.ProviderId)).Append(',');
            csv.Append(CsvEscape(usage.ProviderName)).Append(',');
            csv.Append(usage.RequestsUsed.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(usage.RequestsAvailable.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(usage.UsedPercent.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.AppendLine(usage.IsAvailable ? "true" : "false");
        }

        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        var fileName = $"ai-usage-export-{DateTime.UtcNow:yyyy-MM-dd}.csv";

        return this.File(bytes, "text/csv", fileName);
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }
}
