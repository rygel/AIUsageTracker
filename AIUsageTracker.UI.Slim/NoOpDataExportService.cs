// <copyright file="NoOpDataExportService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;

namespace AIUsageTracker.UI.Slim;

public sealed class NoOpDataExportService : IDataExportService
{
    public Task<string> ExportHistoryToCsvAsync() => Task.FromResult(string.Empty);

    public Task<string> ExportHistoryToJsonAsync() => Task.FromResult(string.Empty);

    public Task<byte[]?> CreateDatabaseBackupAsync() => Task.FromResult<byte[]?>(null);
}
