// <copyright file="PrivacyChangedEventArgs.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

public sealed class PrivacyChangedEventArgs : EventArgs
{
    public PrivacyChangedEventArgs(bool isPrivacyMode)
    {
        this.IsPrivacyMode = isPrivacyMode;
    }

    public bool IsPrivacyMode { get; }
}
