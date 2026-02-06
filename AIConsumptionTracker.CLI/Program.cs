using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Core.Services;
using AIConsumptionTracker.Infrastructure.Configuration;
using AIConsumptionTracker.Infrastructure.Providers;
using AIConsumptionTracker.Infrastructure.Helpers;
using AIConsumptionTracker.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Diagnostics;

namespace AIConsumptionTracker.CLI;

class Program
{
    static async Task Main(string[] args)
    {
        try 
        {
            await Run(args);
        }
        finally
        {
            Process.GetCurrentProcess().Kill();
        }
    }

    static async Task Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: opencode-tracker <command> [options]");
            Console.WriteLine("Commands:");
            Console.WriteLine("  status    Show usage status");
            Console.WriteLine("    --all   Show all providers even if not configured");
            Console.WriteLine("    --json  Output as JSON");
            Console.WriteLine("  list      List configured providers");
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
            configure.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
        });

        services.AddHttpClient();
        services.AddSingleton<IConfigLoader, JsonConfigLoader>();
        services.AddTransient<IProviderService, SimulatedProvider>();
        services.AddTransient<IProviderService, OpenCodeProvider>();
        services.AddTransient<IProviderService, ZaiProvider>();
        services.AddTransient<IProviderService, OpenRouterProvider>();
        services.AddTransient<IProviderService, AntigravityProvider>();
        services.AddTransient<IProviderService, GeminiProvider>();
        services.AddTransient<IProviderService, KimiProvider>();
        services.AddTransient<IProviderService, OpenCodeZenProvider>();
        services.AddTransient<IProviderService, DeepSeekProvider>();
        services.AddTransient<IProviderService, OpenAIProvider>();
        services.AddTransient<IProviderService, AnthropicProvider>();
        services.AddTransient<IProviderService, CloudCodeProvider>();
        services.AddTransient<IProviderService, GenericPayAsYouGoProvider>();
        services.AddTransient<IProviderService, GitHubCopilotProvider>();
        
        services.AddSingleton<ProviderManager>();

        var serviceProvider = services.BuildServiceProvider();
        var manager = serviceProvider.GetRequiredService<ProviderManager>();
        var configLoader = serviceProvider.GetRequiredService<IConfigLoader>();

        switch (command)
        {
            case "status":
                await ShowStatus(manager, json, showAll);
                break;
            case "list":
                await ShowList(configLoader, json);
                break;
            default:
                Console.WriteLine($"Unknown command: {command}");
                break;
        }
    }

    static async Task ShowStatus(ProviderManager manager, bool json, bool showAll)
    {
        var usage = await manager.GetAllUsageAsync();
        
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
                var pct = u.IsAvailable ? $"{u.UsagePercentage:F0}%" : "-";
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

    static async Task ShowList(IConfigLoader loader, bool json)
    {
        var configs = await loader.LoadConfigAsync();
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

