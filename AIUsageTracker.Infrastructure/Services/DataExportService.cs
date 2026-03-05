using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace AIUsageTracker.Infrastructure.Services;

public class DataExportService : IDataExportService
{
    private readonly IWebDatabaseRepository _repository;
    private readonly ILogger<DataExportService> _logger;
    private readonly string _dbPath;

    public DataExportService(IWebDatabaseRepository repository, ILogger<DataExportService> logger, string dbPath)
    {
        _repository = repository;
        _logger = logger;
        _dbPath = dbPath;
    }

    public async Task<string> ExportHistoryToCsvAsync()
    {
        try
        {
            var history = await _repository.GetAllHistoryForExportAsync();
            
            var sb = new StringBuilder();
            sb.AppendLine("provider_id,provider_name,requests_used,requests_available,requests_percentage,is_available,status_message,fetched_at,next_reset_time");

            foreach (var row in history)
            {
                var isAvail = row.IsAvailable ? 1 : 0;
                sb.AppendLine($"\"{row.ProviderId}\",\"{row.ProviderName}\",{row.RequestsUsed},{row.RequestsAvailable},{row.RequestsPercentage},{isAvail},\"{row.Description?.Replace("\"", "\"\"")}\",\"{row.FetchedAt:O}\",\"{row.NextResetTime?.ToString("O")}\"");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to CSV");
            return string.Empty;
        }
    }

    public async Task<string> ExportHistoryToJsonAsync()
    {
        try
        {
            var history = await _repository.GetAllHistoryForExportAsync(limit: 10000);
            return JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to JSON");
            return "[]";
        }
    }

    public async Task<byte[]?> CreateDatabaseBackupAsync()
    {
        try
        {
            if (!File.Exists(_dbPath)) return null;
            return await File.ReadAllBytesAsync(_dbPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating database backup");
            return null;
        }
    }
}
