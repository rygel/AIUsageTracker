namespace AIUsageTracker.Core.Interfaces;

public interface IDataExportService
{
    Task<string> ExportHistoryToCsvAsync();
    Task<string> ExportHistoryToJsonAsync();
    Task<byte[]?> CreateDatabaseBackupAsync();
}
