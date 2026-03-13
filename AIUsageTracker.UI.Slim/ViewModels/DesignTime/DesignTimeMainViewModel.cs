// <copyright file="DesignTimeMainViewModel.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.ObjectModel;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim.ViewModels.DesignTime;

/// <summary>
/// Design-time ViewModel for MainWindow, providing sample data for XAML designer.
/// </summary>
/// <remarks>
/// This class uses sample provider IDs for design-time preview. provider-id-guardrail-allow
/// </remarks>
public class DesignTimeMainViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DesignTimeMainViewModel"/> class
    /// with sample data for the XAML designer.
    /// </summary>
    public DesignTimeMainViewModel()
    {
        this.IsLoading = false;
        this.IsPrivacyMode = false;
        this.StatusMessage = "Last updated: 2 minutes ago";
        this.ShowUsedPercentages = false;
        this.IsAlwaysOnTop = true;
        this.LastRefreshTime = DateTime.Now.AddMinutes(-2);

        this.Sections = new ObservableCollection<DesignTimeCollapsibleSectionViewModel>
        {
            CreateQuotaSection(),
            CreatePaygoSection(),
        };
    }

    public bool IsLoading { get; set; }

    public bool IsPrivacyMode { get; set; }

    public string StatusMessage { get; set; }

    public bool ShowUsedPercentages { get; set; }

    public bool IsAlwaysOnTop { get; set; }

    public DateTime LastRefreshTime { get; set; }

    public ObservableCollection<DesignTimeCollapsibleSectionViewModel> Sections { get; set; }

    private static DesignTimeCollapsibleSectionViewModel CreateQuotaSection()
    {
        var section = new DesignTimeCollapsibleSectionViewModel
        {
            Title = "PLANS & QUOTAS",
            IsExpanded = true,
            IsGroupHeader = true,
        };

        section.Items.Add(new DesignTimeProviderCardViewModel
        {
            ProviderId = "claude", // provider-id-guardrail-allow: design-time sample data
            DisplayName = "Claude",
            AccountDisplay = "user@example.com",
            HasAccountName = true,
            ProgressPercentage = 35,
            UsedPercent = 35,
            RemainingPercent = 65,
            ShouldShowProgress = true,
            StatusText = "35% used",
            StatusTone = ProviderCardStatusTone.Secondary,
            IsMissing = false,
            IsQuotaBased = true,
            HasDualQuotaBuckets = false,
            ResetBadgeText = "(6h 30m)",
        });

        section.Items.Add(new DesignTimeProviderCardViewModel
        {
            ProviderId = "chatgpt", // provider-id-guardrail-allow: design-time sample data
            DisplayName = "ChatGPT Plus",
            AccountDisplay = "john.doe@company.com",
            HasAccountName = true,
            ProgressPercentage = 72,
            UsedPercent = 72,
            RemainingPercent = 28,
            ShouldShowProgress = true,
            StatusText = "72% used",
            StatusTone = ProviderCardStatusTone.Warning,
            IsMissing = false,
            IsQuotaBased = true,
            HasDualQuotaBuckets = true,
            PrimaryUsedPercent = 72,
            SecondaryUsedPercent = 45,
            ResetBadgeText = "(4h 15m)",
        });

        section.Items.Add(new DesignTimeProviderCardViewModel
        {
            ProviderId = "github-copilot", // provider-id-guardrail-allow: design-time sample data
            DisplayName = "GitHub Copilot",
            AccountDisplay = "developer",
            HasAccountName = true,
            ProgressPercentage = 92,
            UsedPercent = 92,
            RemainingPercent = 8,
            ShouldShowProgress = true,
            StatusText = "92% used",
            StatusTone = ProviderCardStatusTone.Error,
            IsMissing = false,
            IsQuotaBased = true,
            HasDualQuotaBuckets = false,
            ResetBadgeText = "(12h 45m)",
        });

        return section;
    }

    private static DesignTimeCollapsibleSectionViewModel CreatePaygoSection()
    {
        var section = new DesignTimeCollapsibleSectionViewModel
        {
            Title = "PAY AS YOU GO",
            IsExpanded = true,
            IsGroupHeader = true,
        };

        section.Items.Add(new DesignTimeProviderCardViewModel
        {
            ProviderId = "openai", // provider-id-guardrail-allow: design-time sample data
            DisplayName = "OpenAI API",
            AccountDisplay = "org-12345",
            HasAccountName = true,
            ProgressPercentage = 15,
            UsedPercent = 15,
            RemainingPercent = 85,
            ShouldShowProgress = true,
            StatusText = "$12.50 / $100",
            StatusTone = ProviderCardStatusTone.Secondary,
            IsMissing = false,
            IsQuotaBased = false,
            HasDualQuotaBuckets = false,
            ResetBadgeText = null,
        });

        section.Items.Add(new DesignTimeProviderCardViewModel
        {
            ProviderId = "anthropic", // provider-id-guardrail-allow: design-time sample data
            DisplayName = "Anthropic API",
            AccountDisplay = string.Empty,
            HasAccountName = false,
            ProgressPercentage = 45,
            UsedPercent = 45,
            RemainingPercent = 55,
            ShouldShowProgress = true,
            StatusText = "$45.00 / $100",
            StatusTone = ProviderCardStatusTone.Secondary,
            IsMissing = false,
            IsQuotaBased = false,
            HasDualQuotaBuckets = false,
            ResetBadgeText = null,
        });

        return section;
    }
}
