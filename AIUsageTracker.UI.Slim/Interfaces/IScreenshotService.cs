using System.Windows;

namespace AIUsageTracker.UI.Slim.Interfaces;

public interface IScreenshotService
{
    Task RunHeadlessScreenshotCaptureAsync(string[] args);
    void RenderWindowContent(Window window, string outputPath);
}
