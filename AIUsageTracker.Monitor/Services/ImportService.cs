using System.Text.Json;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Monitor.Services;

public class ImportService
{
    private readonly IUsageDatabase _database;

    public ImportService(IUsageDatabase database)
    {
        _database = database;
    }
`n
    public async Task<(int imported, int skipped, List<string> errors)> ImportHistoryAsync(Stream stream, string format)
    {
        var imported = 0;
        var skipped = 0;
        var errors = new List<string>();

        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                var history = JsonSerializer.Deserialize<List<ProviderUsage>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (history != null)
                {
                    foreach (var item in history)
                    {
                        try
                        {
                            await _database.StoreHistoryAsync(new[] { item });
                            imported++;
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Failed to import item for provider {item.ProviderId}: {ex.Message}");
                            skipped++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to parse JSON: {ex.Message}");
            }
        }
        else if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var reader = new StreamReader(stream);
                var headerLine = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(headerLine))
                {
                    errors.Add("CSV file is empty");
                    return (imported, skipped, errors);
                }

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var values = line.Split(',');
                    if (values.Length < 5)
                    {
                        errors.Add($"Invalid CSV line: {line}");
                        skipped++;
                        continue;
                    }

                    try
                    {
                        var usage = new ProviderUsage
                        {
                            FetchedAt = DateTime.Parse(values[0]),
                            ProviderId = values[1].Trim(),
                            ProviderName = values[1].Trim(),
                            RequestsUsed = double.TryParse(values[3], out var used) ? used : 0,
                            UsageUnit = values[5].Trim()
                        };

                        await _database.StoreHistoryAsync(new[] { usage });
                        imported++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Failed to import CSV line: {ex.Message}");
                        skipped++;
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to parse CSV: {ex.Message}");
            }
        }
        else
        {
            errors.Add($"Unsupported format: {format}");
        }

        return (imported, skipped, errors);
    }
}
