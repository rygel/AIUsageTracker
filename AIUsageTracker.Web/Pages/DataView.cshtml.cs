using AIUsageTracker.Web.Services;
using AIUsageTracker.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIUsageTracker.Web.Pages;

public class DataViewModel : PageModel
{
    private const int PageSize = 100;

    private readonly WebDatabaseService _dbService;
    private readonly IDataExportService _exportService;

    public DataViewModel(WebDatabaseService dbService, IDataExportService exportService)
    {
        this._dbService = dbService;
        this._exportService = exportService;
    }

    public string? TableName { get; set; }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>>? Rows { get; set; }

    public IReadOnlyList<string> Columns { get; set; } = [];

    public int TotalCount { get; set; }

    public int PageNumber { get; set; } = 1;

    public int TotalPages { get; set; }

    public bool IsDatabaseAvailable => this._dbService.IsDatabaseAvailable();

    public async Task<IActionResult> OnGetAsync(string? tableName, int page = 1)
    {
        if (page < 1)
        {
            page = 1;
        }

        this.PageNumber = page;

        // Map URL-friendly names to actual table names
        var actualTable = tableName?.ToLower(System.Globalization.CultureInfo.InvariantCulture) switch
        {
            "providers" => "providers",
            "history" => "provider_history",
            "snapshots" => "raw_snapshots",
            "resets" => "reset_events",
            _ => null,
        };

        if (actualTable == null)
        {
            tableName = "providers";
            actualTable = "providers";
        }

        this.TableName = tableName;

        if (!this.IsDatabaseAvailable)
        {
            return this.Page();
        }

        var (rows, totalCount) = actualTable switch
        {
            "providers" => await this._dbService.GetProvidersRawAsync(page, PageSize).ConfigureAwait(false),
            "provider_history" => await this._dbService.GetProviderHistoryRawAsync(page, PageSize).ConfigureAwait(false),
            "raw_snapshots" => await this._dbService.GetRawSnapshotsRawAsync(page, PageSize).ConfigureAwait(false),
            "reset_events" => await this._dbService.GetResetEventsRawAsync(page, PageSize).ConfigureAwait(false),
            _ => ([], 0),
        };

        this.Rows = rows;
        this.TotalCount = totalCount;
        this.TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);

        if (this.Rows.Any())
        {
            this.Columns = this.Rows.First().Keys.ToList();
        }

        return this.Page();
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        var csv = await this._exportService.ExportHistoryToCsvAsync().ConfigureAwait(false);
        return this.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "history.csv");
    }
}
