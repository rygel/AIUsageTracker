using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIUsageTracker.Web.Pages;

public class DataViewModel : PageModel
{
    private readonly WebDatabaseService _dbService;
    private const int PageSize = 100;

    public DataViewModel(WebDatabaseService dbService)
    {
        _dbService = dbService;
    }

    public string? TableName { get; set; }
    public List<Dictionary<string, object?>>? Rows { get; set; }
    public List<string> Columns { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; } = 1;
    public int TotalPages { get; set; }
    public bool IsDatabaseAvailable => _dbService.IsDatabaseAvailable();

    public async Task<IActionResult> OnGetAsync(string? tableName, int page = 1)
    {
        if (page < 1) page = 1;
        PageNumber = page;

        // Map URL-friendly names to actual table names
        var actualTable = tableName?.ToLower() switch
        {
            "providers" => "providers",
            "history" => "provider_history",
            "snapshots" => "raw_snapshots",
            "resets" => "reset_events",
            _ => null
        };

        if (actualTable == null)
        {
            tableName = "providers";
            actualTable = "providers";
        }

        TableName = tableName;

        if (!IsDatabaseAvailable)
            return Page();

        var (rows, totalCount) = actualTable switch
        {
            "providers" => await _dbService.GetProvidersRawAsync(page, PageSize),
            "provider_history" => await _dbService.GetProviderHistoryRawAsync(page, PageSize),
            "raw_snapshots" => await _dbService.GetRawSnapshotsRawAsync(page, PageSize),
            "reset_events" => await _dbService.GetResetEventsRawAsync(page, PageSize),
            _ => (new List<Dictionary<string, object?>>(), 0)
        };

        Rows = rows;
        TotalCount = totalCount;
        TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);

        if (Rows.Any())
        {
            Columns = Rows.First().Keys.ToList();
        }

        return Page();
    }
}

