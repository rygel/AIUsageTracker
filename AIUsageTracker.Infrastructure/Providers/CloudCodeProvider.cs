using System.Diagnostics;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class CloudCodeProvider : IProviderService
{
    public string ProviderId => "cloud-code";
    private readonly ILogger<CloudCodeProvider> _logger;

    public CloudCodeProvider(ILogger<CloudCodeProvider> logger)
    {
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        var isConnected = false;
        var message = "Not connected";

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
                var psi = new ProcessStartInfo
                {
                    FileName = "gcloud",
                    Arguments = "auth print-access-token",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
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
            }
            catch (Exception ex)
            {
                message = "gcloud not found";
                _logger.LogDebug("gcloud not found: {Message}", ex.Message);
            }
        }

        return new[]
        {
            new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Cloud Code (Google)",
                IsAvailable = isConnected,
                RequestsPercentage = 0.0,
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                Description = message,
                UsageUnit = "Status",
                AuthSource = config.AuthSource
            }
        };
    }
}

