// <copyright file="WpfStateTestCollection.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.UI;

// These tests manipulate global WPF Application state (App.Preferences,
// App.SetPrivacyMode, Application.Current) and must not run in parallel
// with each other to avoid interference.
[CollectionDefinition("WpfState", DisableParallelization = true)]
public sealed class WpfStateTestCollection
{
}
