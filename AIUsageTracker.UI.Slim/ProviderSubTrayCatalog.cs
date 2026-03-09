// <copyright file="ProviderSubTrayCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim
{
    using System.Text.RegularExpressions;
    using AIUsageTracker.Core.Models;

    internal static class ProviderSubTrayCatalog
    {
        public static IReadOnlyList<ProviderUsageDetail> GetEligibleDetails(ProviderUsage? usage)
        {
            if (usage?.Details == null)
            {
                return Array.Empty<ProviderUsageDetail>();
            }

            return usage.Details
                .Where(detail =>
                    !string.IsNullOrWhiteSpace(detail.Name) &&
                    !detail.Name.StartsWith("[", StringComparison.Ordinal) &&
                    IsEligibleDetail(detail))
                .GroupBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(detail => detail.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    `n
        private static bool IsEligibleDetail(ProviderUsageDetail detail)
        {
            if (string.IsNullOrWhiteSpace(detail.Name))
            {
                return false;
            }

            if (detail.DetailType != ProviderUsageDetailType.Model && detail.DetailType != ProviderUsageDetailType.Other)
            {
                return false;
            }

            var match = Regex.Match(detail.Used ?? string.Empty, @"(?<percent>\d+(\.\d+)?)\s*%", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
            return match.Success && double.TryParse(match.Groups["percent"].Value, System.Globalization.CultureInfo.InvariantCulture, out _);
        }
    }
}
