// <copyright file="ErrorStatePresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class ErrorStatePresentationCatalog
{
    public static ErrorStatePresentation Create(bool hasUsages)
    {
        return hasUsages
            ? new ErrorStatePresentation(
                ReplaceProviderCards: false,
                StatusType: StatusType.Warning)
            : new ErrorStatePresentation(
                ReplaceProviderCards: true,
                StatusType: StatusType.Error);
    }
}
