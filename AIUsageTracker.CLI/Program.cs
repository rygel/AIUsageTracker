// <copyright file="Program.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Configuration;
using AIUsageTracker.Infrastructure.Extensions;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.CLI;

public class Program
{
    public static async Task Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using var serviceProvider = CreateServiceProvider();

        // Ensure Agent is running
        var lifecycleService = serviceProvider.GetRequiredService<MonitorLifecycleService>();
        if (!await lifecycleService.IsAgentRunningAsync().ConfigureAwait(false))
        {
            Console.WriteLine("Agent is not running. Attempting to start...");
            if (await lifecycleService.StartAgentAsync().ConfigureAwait(false))
            {
                Console.Write("Waiting for Agent to initialize...");
                if (await lifecycleService.WaitForAgentAsync().ConfigureAwait(false))
                {
                    Console.WriteLine(" Done.");
                }
                else
                {
                    Console.WriteLine(" Failed.");
                    Console.WriteLine("Could not start the Agent service. Please start it manually.");
                    return;
                }
            }
            else
            {
                Console.WriteLine("Failed to launch Agent process.");
                return;
            }
        }

        await RunAsync(args, serviceProvider).ConfigureAwait(false);
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(configure =>
        {
            configure.AddConsole();
            configure.SetMinimumLevel(LogLevel.Warning); // Reduce log noise
        });

        services.AddHttpClient();
        services.AddConfiguredHttpClients();
        services.AddSingleton<IMonitorLauncher, MonitorLauncher>();
        services.AddSingleton<IMonitorService, MonitorService>();
        services.AddSingleton<MonitorLifecycleService>();

        return services.BuildServiceProvider();
    }

    private static async Task RunAsync(string[] args, ServiceProvider serviceProvider)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: act <command> [options]");
            Console.WriteLine("Commands:");
            Console.WriteLine("  status       Show usage status");
            Console.WriteLine("    --all      Show all providers even if not configured");
            Console.WriteLine("    --json     Output as JSON");
            Console.WriteLine("  history      Show usage history");
            Console.WriteLine("    [days]     Number of days to show (default: 7)");
            Console.WriteLine("  list         List configured providers");
            Console.WriteLine("  set-key      Set an API key: set-key <provider-id> [api-key]");
            Console.WriteLine("  remove-key   Remove a provider: remove-key <provider-id>");
            Console.WriteLine("  scan         Scan for API keys from other applications");
            Console.WriteLine("  config       Manage preferences: config [key] [value]");
            Console.WriteLine("  agent        Manage agent: agent <start|stop|restart|info|log>");
            return;
        }

        var command = args[0].ToLower(System.Globalization.CultureInfo.InvariantCulture);
        var showAll = args.Contains("--all", StringComparer.Ordinal);
        var json = args.Contains("--json", StringComparer.Ordinal);

        var agentService = serviceProvider.GetRequiredService<IMonitorService>();
        var lifecycleService = serviceProvider.GetRequiredService<MonitorLifecycleService>();

        switch (command)
        {
            case "status":
                await ShowStatusAsync(agentService, json, showAll).ConfigureAwait(false);
                break;
            case "history":
                int days = 7;
                if (args.Length > 1 && int.TryParse(args[1], System.Globalization.CultureInfo.InvariantCulture, out int d))
                {
                    days = d;
                }

                await ShowHistoryAsync(agentService, days, json).ConfigureAwait(false);
                break;
            case "list":
                await ShowListAsync(agentService, json).ConfigureAwait(false);
                break;
            case "set-key":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: act set-key <provider-id> [api-key]");
                    Console.WriteLine("  If api-key is omitted, you will be prompted to enter it.");
                    return;
                }

                string apiKeyArg;
                if (args.Length >= 3)
                {
                    apiKeyArg = args[2];
                }
                else
                {
                    Console.Write($"Enter API key for '{args[1]}': ");
                    apiKeyArg = Console.ReadLine() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(apiKeyArg))
                    {
                        Console.WriteLine("No key entered. Aborting.");
                        return;
                    }
                }

                await SetKeyAsync(agentService, args[1], apiKeyArg).ConfigureAwait(false);
                break;
            case "remove-key":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: act remove-key <provider-id>");
                    return;
                }

                await RemoveKeyAsync(agentService, args[1]).ConfigureAwait(false);
                break;
            case "scan":
                await ScanKeysAsync(agentService).ConfigureAwait(false);
                break;
            case "config":
                if (args.Length == 1)
                {
                    await ShowConfigAsync().ConfigureAwait(false);
                }
                else if (args.Length >= 3)
                {
                    await SetConfigAsync(args[1], args[2]).ConfigureAwait(false);
                }
                else
                {
                    Console.WriteLine("Usage: act config [key] [value]");
                }

                break;
            case "agent":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: act agent <start|stop|restart|info|log>");
                    return;
                }

                await ManageAgentAsync(lifecycleService, args[1]).ConfigureAwait(false);
                break;
            case "check":
                string? providerId = args.Length > 1 ? args[1] : null;
                await CheckProviderAsync(agentService, providerId).ConfigureAwait(false);
                break;
            case "export":
                await ExportDataAsync(agentService, args).ConfigureAwait(false);
                break;
            default:
                Console.WriteLine($"Unknown command: {command}");
                break;
        }
    }

    private static async Task CheckProviderAsync(IMonitorService service, string? providerId)
    {
        if (string.IsNullOrEmpty(providerId))
        {
            Console.WriteLine("Checking all configured providers...");
            var configs = await service.GetConfigsAsync().ConfigureAwait(false);
            foreach (var config in configs)
            {
                await CheckSingleProviderAsync(service, config.ProviderId).ConfigureAwait(false);
            }
        }
        else
        {
            await CheckSingleProviderAsync(service, providerId).ConfigureAwait(false);
        }
    }

    private static async Task CheckSingleProviderAsync(IMonitorService service, string providerId)
    {
        Console.Write($"Checking {providerId}... ");
        var result = await service.CheckProviderAsync(providerId).ConfigureAwait(false);
        if (result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"OK ({result.Message})");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAILED ({result.Message})");
        }

        Console.ResetColor();
    }

    private static async Task ExportDataAsync(IMonitorService service, string[] args)
    {
        string format = "csv";
        int days = 30;
        string output = $"usage_export_{DateTime.Now:yyyyMMdd}.csv";

        for (int i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--format", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                format = args[++i];
            }
            else if (string.Equals(args[i], "--days", StringComparison.Ordinal) && i + 1 < args.Length && int.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out int d))
            {
                days = d;
                i++;
            }
            else if (string.Equals(args[i], "--output", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                output = args[++i];
            }
        }

        // Adjust default extension if format changed but output didn't
        if (string.Equals(format, "json", StringComparison.Ordinal) && output.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            output = Path.ChangeExtension(output, ".json");
        }

        Console.WriteLine($"Exporting {days} days of history to {output} ({format})...");

        var stream = await service.ExportDataAsync(format, days).ConfigureAwait(false);
        if (stream != null)
        {
            using var fileStream = File.Create(output);
            await stream.CopyToAsync(fileStream).ConfigureAwait(false);
            Console.WriteLine("Export complete.");
        }
        else
        {
            Console.WriteLine("Export failed.");
        }
    }

    private static async Task ShowHistoryAsync(IMonitorService service, int days, bool json)
    {
        // For CLI simplicity, we'll just show the last N entries or a summary if possible.
        // The Agent API currently supports ?limit=N.
        // Ideally, we'd have a 'days' parameter on the API, but limit works for now.
        // Assuming ~50 requests/day for a heavy user, 7 days = 350.
        var limit = days * 50;
        var history = await service.GetHistoryAsync(limit).ConfigureAwait(false);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        if (!history.Any())
        {
            Console.WriteLine("No history found.");
            return;
        }

        Console.WriteLine($"History (Last {history.Count} requests):");
        Console.WriteLine($"{"Time",-12} | {"Provider",-20} | {"Model",-25} | {"Used",-15}");
        Console.WriteLine(new string('-', 78));

        foreach (var item in history)
        {
            var used = item.IsCurrencyUsage
                ? $"${item.RequestsUsed:F2}"
                : item.RequestsUsed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var providerDisplayName = ProviderMetadataCatalog.ResolveDisplayLabel(item.ProviderId, item.ProviderName);
            Console.WriteLine($"{item.FetchedAt.ToShortDateString(),-12} | {providerDisplayName,-20} | {"(Total)",-25} | {used,-15}");
        }
    }

    private static async Task SetKeyAsync(IMonitorService service, string providerId, string apiKey)
    {
        Console.WriteLine($"Setting key for '{providerId}'...");

        var configs = await service.GetConfigsAsync().ConfigureAwait(false);
        var existingConfig = configs.FirstOrDefault(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

        if (existingConfig != null)
        {
            existingConfig.ApiKey = apiKey;
            if (await service.SaveConfigAsync(existingConfig).ConfigureAwait(false))
            {
                Console.WriteLine("Key updated successfully.");
                await service.TriggerRefreshAsync().ConfigureAwait(false);
            }
            else
            {
                Console.WriteLine("Failed to update key.");
            }
        }
        else
        {
            var newConfig = new ProviderConfig
            {
                ProviderId = providerId,
                ApiKey = apiKey,
            };

            if (await service.SaveConfigAsync(newConfig).ConfigureAwait(false))
            {
                Console.WriteLine("Key saved successfully.");
                await service.TriggerRefreshAsync().ConfigureAwait(false);
            }
            else
            {
                Console.WriteLine("Failed to save key.");
            }
        }
    }

    private static async Task RemoveKeyAsync(IMonitorService service, string providerId)
    {
        Console.WriteLine($"Removing key for '{providerId}'...");
        if (await service.RemoveConfigAsync(providerId).ConfigureAwait(false))
        {
            Console.WriteLine("Key removed successfully.");
            await service.TriggerRefreshAsync().ConfigureAwait(false);
        }
        else
        {
            Console.WriteLine("Failed to remove key.");
        }
    }

    private static async Task ScanKeysAsync(IMonitorService service)
    {
        Console.WriteLine("Scanning for API keys from known applications...");
        var result = await service.ScanForKeysAsync().ConfigureAwait(false);

        if (result.Count > 0)
        {
            Console.WriteLine($"Found {result.Count} new API keys:");
            foreach (var config in result.Configs)
            {
                Console.WriteLine($" - {config.ProviderId}");
            }

            Console.WriteLine("Keys have been saved to configuration.");
            await service.TriggerRefreshAsync().ConfigureAwait(false);
        }
        else
        {
            Console.WriteLine("No new API keys found.");
        }
    }

    private static async Task ShowConfigAsync()
    {
        var loader = new JsonConfigLoader();
        var prefs = await loader.LoadPreferencesAsync().ConfigureAwait(false);
        Console.WriteLine("Current Configuration:");
        Console.WriteLine(JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task SetConfigAsync(string key, string value)
    {
        var loader = new JsonConfigLoader();
        var prefs = await loader.LoadPreferencesAsync().ConfigureAwait(false);

        // Reflection to set property
        var prop = prefs.GetType().GetProperty(key, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (prop == null)
        {
            Console.WriteLine($"Configuration key '{key}' not found.");
            return;
        }

        try
        {
            object? typedValue = null;
            if (prop.PropertyType == typeof(bool))
            {
                typedValue = bool.Parse(value);
            }
            else if (prop.PropertyType == typeof(int))
            {
                typedValue = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (prop.PropertyType == typeof(double))
            {
                typedValue = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (prop.PropertyType.IsEnum)
            {
                typedValue = Enum.Parse(prop.PropertyType, value, true);
            }

            if (typedValue != null)
            {
                prop.SetValue(prefs, typedValue);
                await loader.SavePreferencesAsync(prefs).ConfigureAwait(false);
                Console.WriteLine($"Configuration '{key}' updated to '{value}'.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to set value: {ex.Message}");
        }
    }

    private static async Task ManageAgentAsync(MonitorLifecycleService lifecycleService, string action)
    {
        switch (action.ToLower(System.Globalization.CultureInfo.InvariantCulture))
        {
            case "info":
                var port = await lifecycleService.GetAgentPortAsync().ConfigureAwait(false);
                var running = await lifecycleService.IsAgentRunningAsync().ConfigureAwait(false);
                Console.WriteLine($"Agent Status: {(running ? "Running" : "Stopped")}");
                Console.WriteLine($"Port: {port}");
                break;
            case "stop":
                Console.WriteLine("Stopping Agent...");
                if (await lifecycleService.StopAgentAsync().ConfigureAwait(false))
                {
                    Console.WriteLine("Agent stopped.");
                }
                else
                {
                    Console.WriteLine("Failed to stop Agent.");
                }

                break;
            case "start":
                Console.WriteLine("Starting Agent...");
                if (await lifecycleService.StartAgentAsync().ConfigureAwait(false))
                {
                    Console.WriteLine("Agent started.");
                }
                else
                {
                    Console.WriteLine("Failed to start Agent.");
                }

                break;
            case "restart":
                Console.WriteLine("Restarting Agent...");
                await lifecycleService.StopAgentAsync().ConfigureAwait(false);
                await Task.Delay(1000).ConfigureAwait(false); // Wait a bit
                if (await lifecycleService.StartAgentAsync().ConfigureAwait(false))
                {
                    Console.WriteLine("Agent restarted.");
                }
                else
                {
                    Console.WriteLine("Failed to restart Agent.");
                }

                break;
            default:
                Console.WriteLine($"Unknown agent command: {action}");
                break;
        }
    }

    private static async Task ShowStatusAsync(IMonitorService service, bool json, bool showAll)
    {
        var usage = await service.GetUsageAsync().ConfigureAwait(false);

        if (!showAll)
        {
            usage = usage.Where(u => u.IsAvailable).ToList();
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(usage, AppJsonContext.Default.ListProviderUsage));
        }
        else
        {
            Console.WriteLine($"{"Provider",-36} | {"Type",-14} | {"Used",-10} | {"Description"}");
            Console.WriteLine(new string('-', 98));

            if (!usage.Any())
            {
                Console.WriteLine("No active providers found.");
                if (!showAll)
                {
                    Console.WriteLine("Use --all to see all configured providers.");
                }
            }

            foreach (var u in usage)
            {
                var usedPct = u.UsedPercent;
                var pct = u.IsAvailable ? $"{usedPct:F0}%" : "-";

                // Handle missing PlanType or IsQuotaBased if relying on serialized data
                var type = u.IsQuotaBased ? "Quota" : "Pay-As-You-Go";
                var accountInfo = !string.IsNullOrWhiteSpace(u.AccountName) ? $" [{u.AccountName}]" : string.Empty;
                var providerDisplayName = ProviderMetadataCatalog.ResolveDisplayLabel(u.ProviderId, u.ProviderName);

                var description = u.Description;

                // Append account to description (first line)
                if (string.IsNullOrEmpty(description))
                {
                    description = accountInfo.Trim();
                }
                else
                {
                    // If existing desc, append
                    description += accountInfo;
                }

                var lines = description.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                Console.WriteLine($"{providerDisplayName,-36} | {type,-14} | {pct,-10} | {lines[0]}");

                for (int i = 1; i < lines.Length; i++)
                {
                    Console.WriteLine($"{string.Empty,-36} | {string.Empty,-14} | {string.Empty,-10} | {lines[i]}");
                }

            }
        }
    }

    private static async Task ShowListAsync(IMonitorService service, bool json)
    {
        var configs = await service.GetConfigsAsync().ConfigureAwait(false);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(configs, AppJsonContext.Default.ListProviderConfig));
        }
        else
        {
            foreach (var c in configs)
            {
                Console.WriteLine($"ID: {c.ProviderId}, Name: {ProviderMetadataCatalog.ResolveDisplayLabel(c.ProviderId)}");
            }
        }
    }
}
