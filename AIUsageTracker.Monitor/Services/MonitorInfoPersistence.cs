// <copyright file="MonitorInfoPersistence.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

internal static class MonitorInfoPersistence
{
    public static void SaveMonitorInfo(int port, bool debug, ILogger logger, IAppPathProvider pathProvider, string? startupStatus = null)
    {
        var info = new MonitorInfo
        {
            Port = port,
            StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ProcessId = Environment.ProcessId,
            DebugMode = debug,
            Errors = new List<string>(),
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
        };

        if (!string.IsNullOrEmpty(startupStatus))
        {
            var errors = info.Errors?.ToList() ?? new List<string>();
            errors.Add($"Startup status: {startupStatus}");
            info.Errors = errors;
        }

        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
        var infoPath = pathProvider.GetMonitorInfoFilePath();

        try
        {
            var directory = Path.GetDirectoryName(infoPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(infoPath, json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write monitor info to {MonitorInfoPath}", infoPath);
        }
    }

    public static void ReportError(string message, IAppPathProvider pathProvider, ILogger? logger = null)
    {
        var jsonFile = pathProvider.GetMonitorInfoFilePath();
        if (!File.Exists(jsonFile))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(jsonFile);
            var info = JsonSerializer.Deserialize<MonitorInfo>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (info == null)
            {
                return;
            }

            var errors = info.Errors?.ToList() ?? new List<string>();
            errors.Add(message);
            info.Errors = errors;
            var updatedJson = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(jsonFile, updatedJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger?.LogWarning(ex, "Failed to report error to monitor info");
        }
    }
}
