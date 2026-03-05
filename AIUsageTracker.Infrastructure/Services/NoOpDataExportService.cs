using AIUsageTracker.Core.Interfaces;

namespace AIUsageTracker.Infrastructure.Services;

public class NoOpDataExportService : IDataExportService
{
    public Task<string> ExportHistoryToCsvAsync() => Task.FromResult(string.Empty);
    public Task<string> ExportHistoryToJsonAsync() => Task.FromResult(string.Empty);
    public Task<byte[]?> CreateDatabaseBackupAsync() => Task.FromResult<byte[]?>(null);
}
