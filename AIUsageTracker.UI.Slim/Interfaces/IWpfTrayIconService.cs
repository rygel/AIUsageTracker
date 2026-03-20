using System.Collections.Generic;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim.Interfaces;

public interface IWpfTrayIconService
{
    void Initialize();
    void UpdateProviderTrayIcons(List<ProviderUsage> usages, List<ProviderConfig> configs, AppPreferences? prefs = null);
    void Dispose();
}
