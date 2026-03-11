// <copyright file="IDataExportService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Interfaces;

public interface IDataExportService
{
    Task<string> ExportHistoryToCsvAsync();

    Task<string> ExportHistoryToJsonAsync();

    Task<byte[]?> CreateDatabaseBackupAsync();
}
