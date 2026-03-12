// <copyright file="AgentProviderCapabilityDefinition.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient;

public sealed class AgentProviderCapabilityDefinition
{
    public string ProviderId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool SupportsChildProviderIds { get; set; }

    public bool SupportsAccountIdentity { get; set; }

    public bool ShowInSettings { get; set; } = true;

    public bool CollapseDerivedChildrenInMainWindow { get; set; }

    public bool RenderAggregateDetailsInMainWindow { get; set; }

    public IReadOnlyList<string> HandledProviderIds { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> VisibleDerivedProviderIds { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> SettingsAdditionalProviderIds { get; set; } = Array.Empty<string>();
}
