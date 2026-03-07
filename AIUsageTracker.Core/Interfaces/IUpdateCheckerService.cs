namespace AIUsageTracker.Core.Interfaces;

public interface IUpdateCheckerService
{
    Task<UpdateInfo?> CheckForUpdatesAsync();
    Task<bool> DownloadAndInstallUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null);
}
