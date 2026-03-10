// <copyright file="BudgetStatus.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public class BudgetStatus
{
    public string ProviderId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public double BudgetLimit { get; set; }

    public double CurrentSpend { get; set; }

    public double RemainingBudget { get; set; }

    public double UtilizationPercent { get; set; }

    public BudgetPeriod Period { get; set; }

    public bool IsOverBudget => this.CurrentSpend > this.BudgetLimit;

    public bool IsWarning => this.UtilizationPercent >= 80 && !this.IsOverBudget;

    public bool IsHealthy => this.UtilizationPercent < 80;
}
