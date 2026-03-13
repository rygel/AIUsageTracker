// <copyright file="DesignTimeProviderCardViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.ObjectModel;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim.ViewModels.DesignTime;

/// <summary>
/// Design-time ViewModel for provider cards, providing sample data for XAML designer.
/// </summary>
public class DesignTimeProviderCardViewModel
{
    public DesignTimeProviderCardViewModel()
    {
        this.Details = new ObservableCollection<DesignTimeSubProviderCardViewModel>
        {
            new DesignTimeSubProviderCardViewModel
            {
                DisplayName = "Claude 3.5 Sonnet",
                DisplayValue = "125 / 500",
                IndicatorWidth = 25,
                UsedPercent = 25,
                HasProgress = true,
                ResetText = "(2h 30m)",
            },
            new DesignTimeSubProviderCardViewModel
            {
                DisplayName = "Claude 3 Opus",
                DisplayValue = "45 / 100",
                IndicatorWidth = 45,
                UsedPercent = 45,
                HasProgress = true,
                ResetText = null,
            },
        };
    }

    public string ProviderId { get; set; } = "claude"; // provider-id-guardrail-allow: design-time sample

    public string DisplayName { get; set; } = "Claude";

    public string AccountDisplay { get; set; } = "user@example.com";

    public bool HasAccountName { get; set; } = true;

    public double ProgressPercentage { get; set; } = 35;

    public double UsedPercent { get; set; } = 35;

    public double RemainingPercent { get; set; } = 65;

    public bool ShouldShowProgress { get; set; } = true;

    public string StatusText { get; set; } = "35% used";

    public ProviderCardStatusTone StatusTone { get; set; } = ProviderCardStatusTone.Secondary;

    public bool IsMissing { get; set; }

    public bool IsQuotaBased { get; set; } = true;

    public bool HasDualQuotaBuckets { get; set; }

    public double PrimaryUsedPercent { get; set; }

    public double SecondaryUsedPercent { get; set; }

    public string? ResetBadgeText { get; set; } = "(6h 30m)";

    public DateTime? NextResetTime { get; set; } = DateTime.Now.AddHours(6).AddMinutes(30);

    public string? TooltipContent { get; set; } = "Claude API Usage\nUsed: 35%\nRemaining: 65%";

    public bool HasDetails => this.Details.Count > 0;

    public ObservableCollection<DesignTimeSubProviderCardViewModel> Details { get; set; }

    public int YellowThreshold { get; set; } = 60;

    public int RedThreshold { get; set; } = 80;

    public bool ShowUsedPercentages { get; set; }

    public bool IsPrivacyMode { get; set; }
}
