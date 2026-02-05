using System.Diagnostics;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class CloudCodeProvider : IProviderService
{
    public string ProviderId => "cloud-code";
    private readonly ILogger<CloudCodeProvider> _logger;

    public CloudCodeProvider(ILogger<CloudCodeProvider> logger)
    {
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config)
    {
        // Strategy: 
        // 1. If API Key is provided (unlikely for Cloud Code, usually access token), verify it.
        // 2. If no key, try `gcloud auth print-access-token` to check if user is logged in.
        
        bool isConnected = false;
        string message = "Not connected";

        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            isConnected = true; 
            message = "Configured (Key present)";
        }
        else
        {
            // Try gcloud check
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "gcloud",
                    Arguments = "auth print-access-token",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0)
                    {
                        isConnected = true;
                        message = "Connected (gcloud)";
                    }
                    else
                    {
                         var error = await process.StandardError.ReadToEndAsync();
                         message = $"gcloud Error: {error.Trim()}";
                    }
                }
                else
                {
                    message = "gcloud not found";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to run gcloud");
                message = "gcloud functionality unavailable";
            }
        }

        return new[] { new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "Cloud Code (Google)",
            IsAvailable = isConnected,
            UsagePercentage = 0,
            IsQuotaBased = false,
            PaymentType = PaymentType.UsageBased,
            Description = message,

            UsageUnit = "Status"
        }};
    }
}
