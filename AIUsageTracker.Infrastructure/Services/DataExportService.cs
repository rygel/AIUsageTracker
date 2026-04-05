// <copyright file="DataExportService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text;
using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Services;

public class DataExportService : IDataExportService
{
    private readonly IWebDatabaseRepository _repository;
    private readonly ILogger<DataExportService> _logger;
    private readonly string _dbPath;

    public DataExportService(IWebDatabaseRepository repository, ILogger<DataExportService> logger, string dbPath)
    {
        this._repository = repository;
        this._logger = logger;
        this._dbPath = dbPath;
    }

    public async Task<string> ExportHistoryToCsvAsync()
    {
        try
        {
            var history = await this._repository.GetAllHistoryForExportAsync().ConfigureAwait(false);

            var sb = new StringBuilder();
            sb.AppendLine("provider_id,provider_name,requests_used,requests_available,requests_percentage,is_available,status_message,fetched_at,next_reset_time");

            foreach (var row in history)
            {
                var isAvail = row.IsAvailable ? 1 : 0;
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"{0}\",\"{1}\",{2},{3},{4},{5},\"{6}\",\"{7:O}\",\"{8:O}\"\r\n", row.ProviderId, row.ProviderName, row.RequestsUsed, row.RequestsAvailable, row.UsedPercent, isAvail, row.Description?.Replace("\"", "\"\"", StringComparison.Ordinal), row.FetchedAt, row.NextResetTime?.ToString("O"));
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error exporting to CSV");
            return string.Empty;
        }
    }

    public async Task<string> ExportHistoryToJsonAsync()
    {
        try
        {
            var history = await this._repository.GetAllHistoryForExportAsync(limit: 10000).ConfigureAwait(false);
            return JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error exporting to JSON");
            return "[]";
        }
    }

    public async Task<byte[]?> CreateDatabaseBackupAsync()
    {
        try
        {
            if (!File.Exists(this._dbPath))
            {
                return null;
            }

            return await File.ReadAllBytesAsync(this._dbPath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error creating database backup");
            return null;
        }
    }
}
