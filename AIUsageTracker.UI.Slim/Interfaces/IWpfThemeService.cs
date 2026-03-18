using System.Windows;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim.Interfaces;

public interface IWpfThemeService
{
    void ApplyTheme(AppTheme theme);
    void ApplyTheme(Window window);
    void ApplyTheme(Window window, string themeName);
}
