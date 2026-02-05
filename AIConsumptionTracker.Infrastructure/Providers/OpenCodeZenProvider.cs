using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class OpenCodeZenProvider : IProviderService
{
    public string ProviderId => "opencode-zen";
    private readonly ILogger<OpenCodeZenProvider> _logger;
    private readonly string _cliPath;

    public OpenCodeZenProvider(ILogger<OpenCodeZenProvider> logger)
    {
        _logger = logger;
        // Hardcoded path based on discovery
        // In a real app, this should be configurable or determined via PATH/config
        _cliPath = @"C:\Users\Alexander\AppData\Roaming\npm\opencode.cmd";
    }

    public Task<ProviderUsage> GetUsageAsync(ProviderConfig config)
    {
        // Execute CLI synchronously (it's fast enough or we accept the block on thread pool)
        // Better to use Task.Run for process execution
        return Task.Run(() => 
        {
            if (!File.Exists(_cliPath))
            {
                return new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "OpenCode Zen",
                    IsAvailable = false,
                    Description = "CLI not found at expected path"
                };
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _cliPath,
                    Arguments = "stats --days 7 --models 10",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                     throw new Exception("Failed to start opencode process");
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit(5000); // 5 sec timeout

                if (process.ExitCode != 0)
                {
                    _logger.LogWarning($"OpenCode CLI failed: {error}");
                     return new ProviderUsage
                    {
                        ProviderId = ProviderId,
                        ProviderName = "OpenCode Zen",
                        IsAvailable = false,
                        Description = $"CLI Error: {process.ExitCode} (Check log or clear storage if JSON error)"
                    };
                }

                return ParseOutput(output);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run OpenCode CLI");
                return new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "OpenCode Zen",
                    IsAvailable = false,
                    Description = $"Execution Failed: {ex.Message}"
                };
            }
        });
    }

    private ProviderUsage ParseOutput(string output)
    {
        // Example Output patterns based on Swift reference:
        // │Total Cost   $12.34
        // │Avg Cost/Day $1.23
        // │Sessions     123
        
        double totalCost = 0;
        double avgCost = 0;

        // Clean ANSI codes if any (simplified check)
        // Parsing regex
        var costMatch = Regex.Match(output, @"Total Cost\s+\$([0-9.]+)");
        if (costMatch.Success)
        {
            double.TryParse(costMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out totalCost);
        }

        var avgMatch = Regex.Match(output, @"Avg Cost/Day\s+\$([0-9.]+)");
        if (avgMatch.Success)
        {
            double.TryParse(avgMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out avgCost);
        }

        // Description
        // "Usage: $12.34 (7 days)"
        
        return new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "OpenCode Zen",
            UsagePercentage = 0, // Pay as you go, no limit percentage usually
            CostUsed = totalCost,
            CostLimit = 0,
            UsageUnit = "USD",
            IsQuotaBased = false,
            PaymentType = PaymentType.UsageBased,
            IsAvailable = true,

            Description = $"${totalCost:F2} (7 days)"
        };
    }
}

