// <copyright file="CardCatalogPermutations.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

/// <summary>
/// Defines all card setting permutations for the visual documentation catalog.
/// Each permutation captures a screenshot showing how the cards look under that
/// configuration. The catalog is generated in CI via <c>--card-catalog</c>.
/// </summary>
internal static class CardCatalogPermutations
{
    internal static IReadOnlyList<Permutation> All { get; } = BuildPermutations();

    internal static string GenerateMarkdown(IReadOnlyList<(string FileName, string Label, string Description)> captured)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Card Settings Catalog");
        sb.AppendLine();
        sb.AppendLine("Visual reference for every card display setting. Generated automatically in CI.");
        sb.AppendLine();
        sb.AppendLine("| Preview | Setting | Description |");
        sb.AppendLine("|---------|---------|-------------|");

        foreach (var (fileName, label, description) in captured)
        {
            sb.AppendLine($"| ![{label}]({fileName}) | **{label}** | {description} |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Permutation Details");
        sb.AppendLine();

        foreach (var (fileName, label, description) in captured)
        {
            sb.AppendLine($"### {label}");
            sb.AppendLine();
            sb.AppendLine(description);
            sb.AppendLine();
            sb.AppendLine($"![{label}]({fileName})");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static Permutation[] BuildPermutations()
    {
        return new Permutation[]
        {
            // ── Presets ───────────────────────────────────────────────
            new(
                "preset-default",
                "Default",
                "Factory defaults: PaceBadge, UsageRate, StatusText, ResetAbsolute. Pace-aware colours on, background bar on.",
                p => ApplyDefaults(p)),

            new(
                "preset-compact",
                "Preset: Compact",
                "Minimal layout: UsedPercent badge only, no secondary info, no status line. ResetAbsolute for reset time.",
                p =>
                {
                    ApplyDefaults(p);
                    p.CardPrimaryBadge = CardSlotContent.UsedPercent;
                    p.CardSecondaryBadge = CardSlotContent.None;
                    p.CardStatusLine = CardSlotContent.None;
                    p.CardResetInfo = CardSlotContent.ResetAbsolute;
                }),

            new(
                "preset-detailed",
                "Preset: Detailed",
                "Full information: PaceBadge, UsageRate, StatusText, ResetAbsolute.",
                p =>
                {
                    ApplyDefaults(p);
                    p.CardPrimaryBadge = CardSlotContent.PaceBadge;
                    p.CardSecondaryBadge = CardSlotContent.UsageRate;
                    p.CardStatusLine = CardSlotContent.StatusText;
                    p.CardResetInfo = CardSlotContent.ResetAbsolute;
                }),

            new(
                "preset-pace-focus",
                "Preset: Pace Focus",
                "Pace-centred: PaceBadge, ProjectedPercent at end of period, DailyBudget, ResetAbsolute.",
                p =>
                {
                    ApplyDefaults(p);
                    p.CardPrimaryBadge = CardSlotContent.PaceBadge;
                    p.CardSecondaryBadge = CardSlotContent.ProjectedPercent;
                    p.CardStatusLine = CardSlotContent.DailyBudget;
                    p.CardResetInfo = CardSlotContent.ResetAbsolute;
                }),

            // ── Layout toggles ────────────────────────────────────────
            new(
                "compact-mode",
                "Compact Mode",
                "Reduced card height (20px instead of 24px), tighter margins and smaller fonts.",
                p =>
                {
                    ApplyDefaults(p);
                    p.CardCompactMode = true;
                }),

            new(
                "no-background-bar",
                "Background Bar Off",
                "Progress bar replaced by a thin 3px colour stripe on the left edge of each card.",
                p =>
                {
                    ApplyDefaults(p);
                    p.CardBackgroundBar = false;
                }),

            new(
                "compact-no-bg",
                "Compact + No Background Bar",
                "Compact mode combined with colour stripe. Most minimal card appearance.",
                p =>
                {
                    ApplyDefaults(p);
                    p.CardCompactMode = true;
                    p.CardBackgroundBar = false;
                }),

            // ── Dual quota bars ───────────────────────────────────────
            new(
                "dual-bars-on",
                "Dual Quota Bars",
                "Providers with burst + rolling windows show two stacked progress bars. Top bar = burst (5h), bottom bar = rolling (weekly).",
                p =>
                {
                    ApplyDefaults(p);
                    p.ShowDualQuotaBars = true;
                }),

            new(
                "dual-bars-off-rolling",
                "Dual Bars Off (Rolling)",
                "Dual bars disabled, single bar shows the rolling (weekly) window.",
                p =>
                {
                    ApplyDefaults(p);
                    p.ShowDualQuotaBars = false;
                    p.DualQuotaSingleBarMode = DualQuotaSingleBarMode.Rolling;
                }),

            new(
                "dual-bars-off-burst",
                "Dual Bars Off (Burst)",
                "Dual bars disabled, single bar shows the burst (5-hour) window.",
                p =>
                {
                    ApplyDefaults(p);
                    p.ShowDualQuotaBars = false;
                    p.DualQuotaSingleBarMode = DualQuotaSingleBarMode.Burst;
                }),

            // ── Display mode ──────────────────────────────────────────
            new(
                "show-used",
                "Show Used Percentages",
                "Displays \"45% used\" instead of \"55% remaining\". Progress bars fill from left (used) instead of depleting from right.",
                p =>
                {
                    ApplyDefaults(p);
                    p.ShowUsedPercentages = true;
                }),

            // ── Pace adjustment ───────────────────────────────────────
            new(
                "pace-off",
                "Pace Adjustment Off",
                "Bar colour based on raw usage percentage. No projection to end of period. A provider at 30% used always shows green, even if only 1 hour remains.",
                p =>
                {
                    ApplyDefaults(p);
                    p.EnablePaceAdjustment = false;
                }),

            // ── Reset time display ────────────────────────────────────
            new(
                "reset-relative",
                "Relative Reset Time",
                "Reset time shown as a countdown (\"in 4h 23m\") instead of an absolute timestamp.",
                p =>
                {
                    ApplyDefaults(p);
                    p.UseRelativeResetTime = true;
                }),

            new(
                "reset-date",
                "Reset Date Only",
                "Reset info slot shows the date portion only (no time).",
                p =>
                {
                    ApplyDefaults(p);
                    p.CardResetInfo = CardSlotContent.ResetAbsoluteDate;
                }),

            // ── Badge slot variations ─────────────────────────────────
            new(
                "slots-account-auth",
                "Slots: Account + Auth Source",
                "Primary badge shows account name (privacy-masked), secondary shows auth source.",
                p =>
                {
                    ApplyDefaults(p);
                    p.CardPrimaryBadge = CardSlotContent.AccountName;
                    p.CardSecondaryBadge = CardSlotContent.AuthSource;
                    p.CardStatusLine = CardSlotContent.UsedPercent;
                    p.CardResetInfo = CardSlotContent.ResetRelative;
                }),

            new(
                "slots-remaining-budget",
                "Slots: Remaining + Daily Budget",
                "Shows remaining percentage and daily budget (how many requests per day to stay on pace).",
                p =>
                {
                    ApplyDefaults(p);
                    p.CardPrimaryBadge = CardSlotContent.RemainingPercent;
                    p.CardSecondaryBadge = CardSlotContent.DailyBudget;
                    p.CardStatusLine = CardSlotContent.None;
                    p.CardResetInfo = CardSlotContent.ResetAbsolute;
                }),
        };
    }

    private static void ApplyDefaults(AppPreferences p)
    {
        p.CardCompactMode = false;
        p.CardBackgroundBar = true;
        p.ShowDualQuotaBars = true;
        p.DualQuotaSingleBarMode = DualQuotaSingleBarMode.Rolling;
        p.EnablePaceAdjustment = true;
        p.ShowUsedPercentages = false;
        p.ShowUsagePerHour = false;
        p.UseRelativeResetTime = false;
        p.CardPrimaryBadge = CardSlotContent.PaceBadge;
        p.CardSecondaryBadge = CardSlotContent.UsageRate;
        p.CardStatusLine = CardSlotContent.StatusText;
        p.CardResetInfo = CardSlotContent.ResetAbsolute;
        p.ColorThresholdYellow = 60;
        p.ColorThresholdRed = 80;
    }

    internal sealed record Permutation(
        string Slug,
        string Label,
        string Description,
        Action<AppPreferences> Apply);
}
