using AIUsageTracker.Core.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AIUsageTracker.Monitor.Services;

public class ExportService
{
    private readonly IUsageDatabase _database;

    public ExportService(IUsageDatabase database)
    {
        _database = database;
    }

    public async Task<(byte[] content, string contentType, string fileName)> ExportAsync(string format, int days)
    {
        // Limit days to reasonable range
        if (days < 1) days = 1;
        if (days > 365) days = 365;

        // Estimate limit based on days (assuming ~100 requests/day max for safety)
        var limit = days * 100;
        var history = await _database.GetHistoryAsync(limit);
        
        // Filter by date
        var cutoff = DateTime.UtcNow.AddDays(-days);
        history = history.Where(h => h.FetchedAt >= cutoff).ToList();

        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            var json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
            return (Encoding.UTF8.GetBytes(json), "application/json", $"usage_export_{DateTime.Now:yyyyMMdd}.json");
        }
        else
        {
            var csv = new StringBuilder();
            csv.AppendLine("Time,Provider,Model,Used,Cost,Unit,PlanType");

            foreach (var item in history)
            {
                var time = item.FetchedAt.ToString("yyyy-MM-dd HH:mm:ss");
                var provider = EscapeCsv(item.ProviderName);
                
                if (item.Details != null && item.Details.Any())
                {
                    foreach (var detail in item.Details)
                    {
                        var model = EscapeCsv(detail.Name);
                        var used = EscapeCsv(detail.Used);
                        // Try to parse cost if possible, or just dump used
                        // The detail.Used often contains the unit, so we might inevitably duplicate it or just leave it as string
                        csv.AppendLine($"{time},{provider},{model},{used},,{item.UsageUnit},{item.PlanType}");
                    }
                }
                else
                {
                    var used = item.RequestsUsed.ToString("F2", CultureInfo.InvariantCulture);
                    csv.AppendLine($"{time},{provider},(Total),{used},,{item.UsageUnit},{item.PlanType}");
                }
            }
            
            return (Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"usage_export_{DateTime.Now:yyyyMMdd}.csv");
        }
    }

    private static string EscapeCsv(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}


