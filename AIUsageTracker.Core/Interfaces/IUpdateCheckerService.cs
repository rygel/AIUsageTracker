// <copyright file="IUpdateCheckerService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Interfaces
{
    public interface IUpdateCheckerService
    {
        Task<UpdateInfo?> CheckForUpdatesAsync();

        Task<bool> DownloadAndInstallUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null);
    }
}
