// <copyright file="ConfigPathEntry.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Paths;

public readonly record struct ConfigPathEntry(
    string Path,
    ConfigPathKind Kind);
