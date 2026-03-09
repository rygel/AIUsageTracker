// <copyright file="Reliability.cshtml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Web.Pages
{
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Web.Services;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.RazorPages;
    using Microsoft.AspNetCore.OutputCaching;

    [OutputCache(PolicyName = "ReliabilityCache")]
    public class ReliabilityModel : PageModel
    {
        private readonly WebDatabaseService _dbService;
        private readonly IUsageAnalyticsService _analyticsService;

        public ReliabilityModel(WebDatabaseService dbService, IUsageAnalyticsService analyticsService)
        {
            this._dbService = dbService;
            this._analyticsService = analyticsService;
        }

        public IReadOnlyList<ProviderUsage>? LatestUsage { get; set; }

        public IReadOnlyDictionary<string, ProviderReliabilitySnapshot> ReliabilityByProvider { get; private set; }
            = new Dictionary<string, ProviderReliabilitySnapshot>(StringComparer.OrdinalIgnoreCase);

        public bool IsDatabaseAvailable => this._dbService.IsDatabaseAvailable();

        public async Task OnGetAsync()
        {
            if (this.IsDatabaseAvailable)
            {
                this.LatestUsage = await this._dbService.GetLatestUsageAsync(includeInactive: true).ConfigureAwait(false);

                if (this.LatestUsage.Count > 0)
                {
                    var providerIds = this.LatestUsage.Select(x => x.ProviderId).ToList();
                    var reliability = await this._analyticsService.GetProviderReliabilityAsync(providerIds).ConfigureAwait(false);
                    this.ReliabilityByProvider = reliability;
                }
            }
        }

        public string GetReliabilityClass(ProviderReliabilitySnapshot snapshot)
        {
            if (!snapshot.IsAvailable || snapshot.FailureRatePercent < 1)
            {
                return "healthy";
            }

            if (snapshot.FailureRatePercent < 10)
            {
                return "warning";
            }

            if (snapshot.FailureRatePercent < 30)
            {
                return "critical";
            }

            return "unknown";
        }

        public string GetReliabilityLabel(ProviderReliabilitySnapshot snapshot)
        {
            if (!snapshot.IsAvailable)
            {
                return "Unknown";
            }

            if (snapshot.FailureRatePercent < 1)
            {
                return "Healthy";
            }

            if (snapshot.FailureRatePercent < 10)
            {
                return "Degraded";
            }

            if (snapshot.FailureRatePercent < 30)
            {
                return "Unhealthy";
            }

            return "Critical";
        }

        public string GetLatencyText(ProviderReliabilitySnapshot snapshot)
        {
            if (!snapshot.IsAvailable || snapshot.SampleCount == 0)
            {
                return "n/a";
            }

            return snapshot.AverageLatencyMs switch
            {
                < 100 => $"{snapshot.AverageLatencyMs:F0}ms",
                < 500 => $"{snapshot.AverageLatencyMs:F0}ms",
                < 1000 => $"{snapshot.AverageLatencyMs / 1000:F1}s",
                _ => $"{snapshot.AverageLatencyMs / 1000:F1}s",
            };
        }

        public string GetLastSyncText(ProviderReliabilitySnapshot snapshot)
        {
            if (!snapshot.IsAvailable || snapshot.LastSuccessfulSyncUtc == null)
            {
                return "Never";
            }

            var elapsed = DateTime.UtcNow - snapshot.LastSuccessfulSyncUtc.Value;

            return elapsed.TotalMinutes switch
            {
                < 1 => "Just now",
                < 60 => $"{(int)elapsed.TotalMinutes}m ago",
                < 1440 => $"{(int)elapsed.TotalHours}h ago",
                _ => $"{(int)elapsed.TotalDays}d ago",
            };
        }
    }
}
