// <copyright file="UpdateInfo.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Interfaces;

public class UpdateInfo
{
    public string Version { get; set; } = string.Empty;

    public string ReleaseUrl { get; set; } = string.Empty;

    public string DownloadUrl { get; set; } = string.Empty;

    public string ReleaseNotes { get; set; } = string.Empty;

    public DateTime PublishedAt { get; set; }
}
