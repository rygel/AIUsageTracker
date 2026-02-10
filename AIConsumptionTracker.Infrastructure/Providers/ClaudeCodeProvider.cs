using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class ClaudeCodeProvider : IProviderService
{
    public string ProviderId => "claude-code";
    private readonly ILogger<ClaudeCodeProvider> _logger;

    public ClaudeCodeProvider(ILogger<ClaudeCodeProvider> logger)
    {
        _logger = logger;
    }

    public Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        return Task.Run<IEnumerable<ProviderUsage>>(() => 
        {
            // Check if API key is configured
            if (string.IsNullOrEmpty(config.ApiKey))
            {
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Claude Code",
                    IsAvailable = false,
                    Description = "No API key configured",
                    IsQuotaBased = false,
                    PaymentType = PaymentType.UsageBased
                }};
            }

            // Key is configured, so mark as available
            // Try to get usage from CLI, but don't fail if CLI is not available
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "claude",
                    Arguments = "usage",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    // CLI not found, but key is configured - show as available
                    return new[] { new ProviderUsage
                    {
                        ProviderId = ProviderId,
                        ProviderName = "Claude Code",
                        IsAvailable = true,
                        Description = "Connected (API key configured)",
                        IsQuotaBased = false,
                        PaymentType = PaymentType.UsageBased
                    }};
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    _logger.LogWarning($"Claude Code CLI failed: {error}");
                    // CLI failed, but key is configured - show as available
                    return new[] { new ProviderUsage
                    {
                        ProviderId = ProviderId,
                        ProviderName = "Claude Code",
                        IsAvailable = true,
                        Description = "Connected (API key configured)",
                        IsQuotaBased = false,
                        PaymentType = PaymentType.UsageBased
                    }};
                }

                return new[] { ParseOutput(output) };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run Claude Code CLI");
                // Exception occurred, but key is configured - show as available
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Claude Code",
                    IsAvailable = true,
                    Description = "Connected (API key configured)",
                    IsQuotaBased = false,
                    PaymentType = PaymentType.UsageBased
                }};
            }
        });
    }

    private ProviderUsage ParseOutput(string output)
    {
        // Parse Claude Code usage output
        // Example: 
        // Current Usage: $12.34
        // Budget Limit: $100.00
        // Remaining: $87.66
        
        double currentUsage = 0;
        double budgetLimit = 0;

        var usageMatch = Regex.Match(output, @"Current Usage[:\s]+\$?([0-9.]+)", RegexOptions.IgnoreCase);
        if (usageMatch.Success)
        {
            double.TryParse(usageMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out currentUsage);
        }

        var budgetMatch = Regex.Match(output, @"Budget Limit[:\s]+\$?([0-9.]+)", RegexOptions.IgnoreCase);
        if (budgetMatch.Success)
        {
            double.TryParse(budgetMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out budgetLimit);
        }

        var remainingMatch = Regex.Match(output, @"Remaining[:\s]+\$?([0-9.]+)", RegexOptions.IgnoreCase);
        if (remainingMatch.Success && budgetLimit == 0)
        {
            // If we have remaining but no budget, calculate budget
            double remaining;
            if (double.TryParse(remainingMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out remaining))
            {
                budgetLimit = currentUsage + remaining;
            }
        }

        double usagePercentage = budgetLimit > 0 ? (currentUsage / budgetLimit) * 100.0 : 0;

        return new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "Claude Code",
            UsagePercentage = Math.Min(usagePercentage, 100),
            CostUsed = currentUsage,
            CostLimit = budgetLimit,
            UsageUnit = "USD",
            IsQuotaBased = false,
            PaymentType = PaymentType.UsageBased,
            IsAvailable = true,
            Description = budgetLimit > 0 
                ? $"${currentUsage:F2} used of ${budgetLimit:F2} limit"
                : $"${currentUsage:F2} used"
        };
    }
}
