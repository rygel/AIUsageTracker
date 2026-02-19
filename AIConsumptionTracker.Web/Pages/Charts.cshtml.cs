using AIConsumptionTracker.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIConsumptionTracker.Web.Pages;

public class ChartsModel : PageModel
{
    private readonly AIConsumptionTracker.Core.Interfaces.IConfigLoader _configLoader;
    private readonly WebDatabaseService _dbService;

    public ChartsModel(WebDatabaseService dbService, AIConsumptionTracker.Core.Interfaces.IConfigLoader configLoader)
    {
        _dbService = dbService;
        _configLoader = configLoader;
    }

    public List<ChartDataPoint>? ChartData { get; set; }
    public List<ResetEvent>? ResetEvents { get; set; }
    public Dictionary<string, string> ProviderColors { get; set; } = new();
    public bool IsDatabaseAvailable => _dbService.IsDatabaseAvailable();

    public async Task OnGetAsync(int hours = 24)
    {
        if (IsDatabaseAvailable)
        {
            ChartData = await _dbService.GetChartDataAsync(hours);
            ResetEvents = await _dbService.GetRecentResetEventsAsync(hours);
            
            // Load colors
            var configs = await _configLoader.LoadConfigAsync();
            foreach (var cfg in configs)
            {
                if (cfg.Models != null)
                {
                    foreach (var model in cfg.Models)
                    {
                        if (!string.IsNullOrEmpty(model.Color) && !string.IsNullOrEmpty(model.Name))
                        {
                            ProviderColors[model.Name] = model.Color;
                        }
                    }
                }
            }
        }
    }
}
