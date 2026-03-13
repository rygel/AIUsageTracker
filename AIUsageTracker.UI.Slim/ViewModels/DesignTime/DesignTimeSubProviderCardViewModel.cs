// <copyright file="DesignTimeSubProviderCardViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim.ViewModels.DesignTime;

/// <summary>
/// Design-time ViewModel for sub-provider cards, providing sample data for XAML designer.
/// </summary>
public class DesignTimeSubProviderCardViewModel
{
    public string DisplayName { get; set; } = "Model Name";

    public string DisplayValue { get; set; } = "25 / 100";

    public double IndicatorWidth { get; set; } = 25;

    public double UsedPercent { get; set; } = 25;

    public bool HasProgress { get; set; } = true;

    public string? ResetText { get; set; } = "(2h 30m)";

    public DateTime? NextResetTime { get; set; } = DateTime.Now.AddHours(2).AddMinutes(30);

    public bool IsQuotaBased { get; set; } = true;

    public bool IsPrivacyMode { get; set; }

    public bool ShowUsedPercentages { get; set; }
}
