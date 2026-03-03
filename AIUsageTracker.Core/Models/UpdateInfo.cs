namespace AIUsageTracker.Core.Models;

/// <summary>
/// Represents information about an available update.
/// </summary>
public class UpdateInfo
{
    public string Version { get; set; } = string.Empty;
    public string ReleaseUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
}
