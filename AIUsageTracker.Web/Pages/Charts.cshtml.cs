using AIUsageTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;

namespace AIUsageTracker.Web.Pages;

[OutputCache(PolicyName = "ChartsCache")]
public class ChartsModel : PageModel
{
    private const string ProviderColorsCacheKey = "charts-provider-colors-v1";
    private readonly AIUsageTracker.Core.Interfaces.IConfigLoader _configLoader;
    private readonly WebDatabaseService _dbService;
    private readonly IMemoryCache _memoryCache;

    public ChartsModel(
        WebDatabaseService dbService,
        AIUsageTracker.Core.Interfaces.IConfigLoader configLoader,
        IMemoryCache memoryCache)
    {
        _dbService = dbService;
        _configLoader = configLoader;
        _memoryCache = memoryCache;
    }

    public List<ChartDataPoint>? ChartData { get; set; }
    public Dictionary<string, string> ProviderColors { get; set; } = new();
    public bool IsDatabaseAvailable => _dbService.IsDatabaseAvailable();

    public async Task OnGetAsync(int hours = 24)
    {
        if (IsDatabaseAvailable)
        {
            var chartTask = _dbService.GetChartDataAsync(hours);
            var colorTask = GetProviderColorsAsync();

            await Task.WhenAll(chartTask, colorTask);

            ChartData = chartTask.Result;
            ProviderColors = colorTask.Result;
        }
    }

    public async Task<IActionResult> OnGetResetEventsAsync(int hours = 24)
    {
        if (!IsDatabaseAvailable)
        {
            return new JsonResult(Array.Empty<ResetEvent>());
        }

        var events = await _dbService.GetRecentResetEventsAsync(hours);
        return new JsonResult(events);
    }

    public async Task<IActionResult> OnGetChartPayloadAsync(int hours = 24)
    {
        if (!IsDatabaseAvailable)
        {
            return new JsonResult(new
            {
                chartData = Array.Empty<ChartDataPoint>(),
                resetEvents = Array.Empty<ResetEvent>()
            });
        }

        var chartTask = _dbService.GetChartDataAsync(hours);
        var resetTask = _dbService.GetRecentResetEventsAsync(hours);
        await Task.WhenAll(chartTask, resetTask);

        return new JsonResult(new
        {
            chartData = chartTask.Result,
            resetEvents = resetTask.Result
        });
    }

    private Task<Dictionary<string, string>> GetProviderColorsAsync()
    {
        return _memoryCache.GetOrCreateAsync(ProviderColorsCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var configs = await _configLoader.LoadConfigAsync();
            foreach (var cfg in configs)
            {
                if (cfg.Models == null)
                {
                    continue;
                }

                foreach (var model in cfg.Models)
                {
                    if (!string.IsNullOrEmpty(model.Color) && !string.IsNullOrEmpty(model.Name))
                    {
                        colors[model.Name] = model.Color;
                    }
                }
            }

            return colors;
        })!;
    }
}

