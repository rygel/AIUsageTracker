// <copyright file="WebApiEndpointMapper.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Web.Services
{
    using System.Text;

    using AIUsageTracker.Core.Interfaces;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;

    internal static class WebApiEndpointMapper
    {
        public static void MapMonitorRoutes(IEndpointRouteBuilder app, string routePrefix)
        {
            app.MapGet($"{routePrefix}/status", GetMonitorStatusAsync);
            app.MapPost($"{routePrefix}/start", StartMonitorAsync);
            app.MapPost($"{routePrefix}/stop", StopMonitorAsync);
        }

        public static void MapExportRoutes(IEndpointRouteBuilder app)
        {
            app.MapGet("/api/export/csv", ExportCsvAsync);
            app.MapGet("/api/export/json", ExportJsonAsync);
            app.MapGet("/api/export/backup", ExportBackupAsync);
        }

        private static async Task<IResult> GetMonitorStatusAsync(MonitorProcessService agentService)
        {
            var status = await agentService.GetAgentStatusDetailedAsync().ConfigureAwait(false);
            return Results.Ok(status);
        }

        private static async Task<IResult> StartMonitorAsync(MonitorProcessService agentService)
        {
            var result = await agentService.StartAgentDetailedAsync().ConfigureAwait(false);
            return CreateMonitorActionResult(result);
        }

        private static async Task<IResult> StopMonitorAsync(MonitorProcessService agentService)
        {
            var result = await agentService.StopAgentDetailedAsync().ConfigureAwait(false);
            return CreateMonitorActionResult(result);
        }

        private static async Task<IResult> ExportCsvAsync(IDataExportService exportService)
        {
            var csv = await exportService.ExportHistoryToCsvAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(csv))
            {
                return Results.NotFound("No data to export");
            }

            return Results.Text(csv, "text/csv", Encoding.UTF8);
        }

        private static async Task<IResult> ExportJsonAsync(IDataExportService exportService)
        {
            var json = await exportService.ExportHistoryToJsonAsync().ConfigureAwait(false);
            return Results.Text(json, "application/json", Encoding.UTF8);
        }

        private static async Task<IResult> ExportBackupAsync(IDataExportService exportService)
        {
            var backup = await exportService.CreateDatabaseBackupAsync().ConfigureAwait(false);
            if (backup == null)
            {
                return Results.NotFound("No database to backup");
            }

            return Results.File(backup, "application/octet-stream", $"usage_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        }

        private static IResult CreateMonitorActionResult(MonitorProcessService.MonitorActionResult result)
        {
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        }
    }
}
