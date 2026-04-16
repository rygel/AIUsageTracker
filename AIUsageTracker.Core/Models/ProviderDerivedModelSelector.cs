// <copyright file="ProviderDerivedModelSelector.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public sealed class ProviderDerivedModelSelector
{
    public ProviderDerivedModelSelector(
        string derivedProviderId,
        IEnumerable<string>? modelIdEquals = null,
        IEnumerable<string>? modelIdContains = null,
        IEnumerable<string>? modelNameContains = null)
    {
        if (string.IsNullOrWhiteSpace(derivedProviderId))
        {
            throw new ArgumentException("Derived provider id cannot be empty.", nameof(derivedProviderId));
        }

        this.DerivedProviderId = derivedProviderId;
        this.ModelIdEquals = NormalizeValues(modelIdEquals);
        this.ModelIdContains = NormalizeValues(modelIdContains);
        this.ModelNameContains = NormalizeValues(modelNameContains);
    }

    public string DerivedProviderId { get; }

    public IReadOnlyCollection<string> ModelIdEquals { get; }

    public IReadOnlyCollection<string> ModelIdContains { get; }

    public IReadOnlyCollection<string> ModelNameContains { get; }

    private static string[] NormalizeValues(IEnumerable<string>? values)
    {
        if (values == null)
        {
            return Array.Empty<string>();
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
