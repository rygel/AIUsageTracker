using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.AgentClient;
using AIUsageTracker.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Diagnostics;

namespace AIUsageTracker.CLI;

class Program
{
    static async Task Main(string[] args)
    {
        try 
        {
            // Ensure Agent is running
            if (!await AgentLauncher.IsAgentRunningAsync())
            {
                Console.WriteLine("Agent is not running. Attempting to start...");
                if (await AgentLauncher.StartAgentAsync())
                {
                    Console.Write("Waiting for Agent to initialize...");
                    if (await AgentLauncher.WaitForAgentAsync())
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

            await Run(args);
        }
        finally
        {
            // We don't kill the process anymore since we might have started the shared Agent
        }
    }

    static async Task Run(string[] args)
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
            Console.WriteLine("  set-key      Set an API key: set-key <provider-id> <api-key>");
            Console.WriteLine("  remove-key   Remove a provider: remove-key <provider-id>");
            Console.WriteLine("  scan         Scan for API keys from other applications");
            Console.WriteLine("  config       Manage preferences: config [key] [value]");
            Console.WriteLine("  agent        Manage agent: agent <start|stop|restart|info|log>");
            return;
        }

        var command = args[0].ToLower();
        var showAll = args.Contains("--all");
        var json = args.Contains("--json");

        // Setup DI
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        
        services.AddLogging(configure => 
        {
            configure.AddConsole();
            configure.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning); // Reduce log noise
        });

        services.AddHttpClient();
        services.AddSingleton<AgentService>();

        var serviceProvider = services.BuildServiceProvider();
        var agentService = serviceProvider.GetRequiredService<AgentService>();

        switch (command)
        {
            case "status":
                await ShowStatus(agentService, json, showAll);
                break;
            case "history":
                int days = 7;
                if (args.Length > 1 && int.TryParse(args[1], out int d)) days = d;
                await ShowHistory(agentService, days, json);
                break;
            case "list":
                await ShowList(agentService, json);
                break;
            case "set-key":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: act set-key <provider-id> <api-key>");
                    return;
                }
                await SetKey(agentService, args[1], args[2]);
                break;
            case "remove-key":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: act remove-key <provider-id>");
                    return;
                }
                await RemoveKey(agentService, args[1]);
                break;
            case "scan":
                await ScanKeys(agentService);
                break;
            case "config":
                if (args.Length == 1)
                    await ShowConfig(agentService);
                else if (args.Length >= 3)
                    await SetConfig(agentService, args[1], args[2]);
                else
                    Console.WriteLine("Usage: act config [key] [value]");
                break;
            case "agent":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: act agent <start|stop|restart|info|log>");
                    return;
                }
                await ManageAgent(agentService, args[1]);
                break;
            case "check":
                string? providerId = args.Length > 1 ? args[1] : null;
                await CheckProvider(agentService, providerId);
                break;
            case "export":
                await ExportData(agentService, args);
                break;
            default:
                Console.WriteLine($"Unknown command: {command}");
                break;
        }
    }

    static async Task CheckProvider(AgentService service, string? providerId)
    {
        if (string.IsNullOrEmpty(providerId))
        {
            Console.WriteLine("Checking all configured providers...");
            var configs = await service.GetConfigsAsync();
            foreach (var config in configs)
            {
                await CheckSingleProvider(service, config.ProviderId);
            }
        }
        else
        {
            await CheckSingleProvider(service, providerId);
        }
    }

    static async Task CheckSingleProvider(AgentService service, string providerId)
    {
        Console.Write($"Checking {providerId}... ");
        var (success, message) = await service.CheckProviderAsync(providerId);
        if (success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"OK ({message})");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAILED ({message})");
        }
        Console.ResetColor();
    }

    static async Task ExportData(AgentService service, string[] args)
    {
        string format = "csv";
        int days = 30;
        string output = $"usage_export_{DateTime.Now:yyyyMMdd}.csv";

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--format" && i + 1 < args.Length) format = args[++i];
            else if (args[i] == "--days" && i + 1 < args.Length && int.TryParse(args[i+1], out int d)) { days = d; i++; }
            else if (args[i] == "--output" && i + 1 < args.Length) output = args[++i];
        }

        // Adjust default extension if format changed but output didn't
        if (format == "json" && output.EndsWith(".csv")) output = Path.ChangeExtension(output, ".json");
        
        Console.WriteLine($"Exporting {days} days of history to {output} ({format})...");

        var stream = await service.ExportDataAsync(format, days);
        if (stream != null)
        {
            using var fileStream = File.Create(output);
            await stream.CopyToAsync(fileStream);
            Console.WriteLine("Export complete.");
        }
        else
        {
            Console.WriteLine("Export failed.");
        }
    }

    static async Task ShowHistory(AgentService service, int days, bool json)
    {
        // For CLI simplicity, we'll just show the last N entries or a summary if possible.
        // The Agent API currently supports ?limit=N.
        // Ideally, we'd have a 'days' parameter on the API, but limit works for now.
        // Assuming ~50 requests/day for a heavy user, 7 days = 350.
        var limit = days * 50; 
        var history = await service.GetHistoryAsync(limit);

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
             // Flatten details for simplified view
             if (item.Details != null && item.Details.Any())
             {
                 foreach(var detail in item.Details)
                 {
                      Console.WriteLine($"{item.FetchedAt.ToShortDateString(),-12} | {item.ProviderName,-20} | {detail.Name,-25} | {detail.Used,-15}");
                 }
             }
             else
             {
                 // Fallback for providers without details
                 var used = $"{item.RequestsUsed} {item.UsageUnit}";
                 Console.WriteLine($"{item.FetchedAt.ToShortDateString(),-12} | {item.ProviderName,-20} | {"(Total)",-25} | {used,-15}");
             }
        }
    }

    static async Task SetKey(AgentService service, string providerId, string apiKey)
    {
        Console.WriteLine($"Setting key for '{providerId}'...");
        
        var configs = await service.GetConfigsAsync();
        var existingConfig = configs.FirstOrDefault(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

        if (existingConfig != null)
        {
            existingConfig.ApiKey = apiKey;
            if (await service.SaveConfigAsync(existingConfig))
            {
                Console.WriteLine("Key updated successfully.");
                await service.TriggerRefreshAsync();
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
                ApiKey = apiKey
            };
            
            if (await service.SaveConfigAsync(newConfig))
            {
                Console.WriteLine("Key saved successfully.");
                await service.TriggerRefreshAsync();
            }
            else
            {
                Console.WriteLine("Failed to save key.");
            }
        }
    }

    static async Task RemoveKey(AgentService service, string providerId)
    {
        Console.WriteLine($"Removing key for '{providerId}'...");
        if (await service.RemoveConfigAsync(providerId))
        {
             Console.WriteLine("Key removed successfully.");
             await service.TriggerRefreshAsync();
        }
        else
        {
             Console.WriteLine("Failed to remove key.");
        }
    }

    static async Task ScanKeys(AgentService service)
    {
        Console.WriteLine("Scanning for API keys from known applications...");
        var (count, configs) = await service.ScanForKeysAsync();
        
        if (count > 0)
        {
            Console.WriteLine($"Found {count} new API keys:");
            foreach (var config in configs)
            {
                Console.WriteLine($" - {config.ProviderId}");
            }
            Console.WriteLine("Keys have been saved to configuration.");
            await service.TriggerRefreshAsync();
        }
        else
        {
            Console.WriteLine("No new API keys found.");
        }
    }

    static async Task ShowConfig(AgentService service)
    {
        var prefs = await service.GetPreferencesAsync();
        Console.WriteLine("Current Configuration:");
        Console.WriteLine(JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true }));
    }

    static async Task SetConfig(AgentService service, string key, string value)
    {
        var prefs = await service.GetPreferencesAsync();
        
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
                typedValue = bool.Parse(value);
            else if (prop.PropertyType == typeof(int))
                typedValue = int.Parse(value);
            else if (prop.PropertyType == typeof(double))
                typedValue = double.Parse(value);
            else if (prop.PropertyType == typeof(string))
                typedValue = value;
            else if (prop.PropertyType.IsEnum)
                typedValue = Enum.Parse(prop.PropertyType, value, true);
            
            if (typedValue != null)
            {
                prop.SetValue(prefs, typedValue);
                if (await service.SavePreferencesAsync(prefs))
                    Console.WriteLine($"Configuration '{key}' updated to '{value}'.");
                else
                    Console.WriteLine("Failed to save configuration.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to set value: {ex.Message}");
        }
    }

    static async Task ManageAgent(AgentService service, string action)
    {
        switch (action.ToLower())
        {
            case "info":
                var port = await AgentLauncher.GetAgentPortAsync();
                var running = await AgentLauncher.IsAgentRunningAsync();
                Console.WriteLine($"Agent Status: {(running ? "Running" : "Stopped")}");
                Console.WriteLine($"Port: {port}");
                break;
            case "stop":
                Console.WriteLine("Stopping Agent...");
                if (await AgentLauncher.StopAgentAsync())
                    Console.WriteLine("Agent stopped.");
                else
                    Console.WriteLine("Failed to stop Agent.");
                break;
            case "start":
                Console.WriteLine("Starting Agent...");
                if (await AgentLauncher.StartAgentAsync())
                    Console.WriteLine("Agent started.");
                else
                    Console.WriteLine("Failed to start Agent.");
                break;
            case "restart":
                Console.WriteLine("Restarting Agent...");
                await AgentLauncher.StopAgentAsync();
                await Task.Delay(1000); // Wait a bit
                if (await AgentLauncher.StartAgentAsync())
                    Console.WriteLine("Agent restarted.");
                else
                    Console.WriteLine("Failed to restart Agent.");
                break;
            default:
                Console.WriteLine($"Unknown agent command: {action}");
                break;
        }
    }

    static async Task ShowStatus(AgentService service, bool json, bool showAll)
    {
        var usage = await service.GetUsageAsync();
        
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
               if (!showAll) Console.WriteLine("Use --all to see all configured providers.");
            }

            foreach (var u in usage)
            {
                var pct = u.IsAvailable ? $"{u.RequestsPercentage:F0}%" : "-";
                // Handle missing PlanType or IsQuotaBased if relying on serialized data
                var type = u.IsQuotaBased ? "Quota" : "Pay-As-You-Go";
                var accountInfo = !string.IsNullOrWhiteSpace(u.AccountName) ? $" [{u.AccountName}]" : "";
                
                var description = u.Description;
                if (u.Details != null && u.Details.Any() && string.IsNullOrEmpty(description))
                {
                    description = ""; // Keep generic description empty if details exist
                }
                
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
                
                Console.WriteLine($"{u.ProviderName,-36} | {type,-14} | {pct,-10} | {lines[0]}");
                
                for (int i = 1; i < lines.Length; i++)
                {
                    Console.WriteLine($"{"",-36} | {"",-14} | {"",-10} | {lines[i]}");
                }
                
                if (u.Details != null)
                {
                    foreach (var d in u.Details)
                    {
                        var name = "  " + d.Name;
                        Console.WriteLine($"{name,-36} | {"",-14} | {d.Used,-10} | {d.Description}");
                    }
                }
            }
        }
    }

    static async Task ShowList(AgentService service, bool json)
    {
        var configs = await service.GetConfigsAsync();
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(configs, AppJsonContext.Default.ListProviderConfig));
        }
        else
        {
            foreach (var c in configs)
            {
                Console.WriteLine($"ID: {c.ProviderId}, Type: {c.Type}");
            }
        }
    }
}

