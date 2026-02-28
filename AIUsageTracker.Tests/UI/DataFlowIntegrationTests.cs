using System.Diagnostics;
using System.Net.Http.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;
using Xunit;

namespace AIUsageTracker.Tests.UI;

/// <summary>
/// Integration tests that verify the complete data flow:
/// Monitor -> API -> Slim UI -> Providers displayed
/// </summary>
public class DataFlowIntegrationTests
{
    [Fact]
    public async Task Providers_Appear_In_UI_Within_15_Seconds()
    {
        // This test verifies that when the Monitor has data,
        // the Slim UI displays it within 15 seconds.
        // If this fails, it means the data flow is broken.
        
        var testResult = await RunInStaAsync(async () =>
        {
            EnsureAppCreated();

            var mainWindow = new MainWindow(skipUiInitialization: false);
            mainWindow.Show();
            
            // Wait up to 15 seconds for providers to appear
            var stopwatch = Stopwatch.StartNew();
            var providersFound = false;
            var lastChildCount = 0;
            
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(15))
            {
                // Find the providers list
                var providersList = FindVisualChild<StackPanel>(mainWindow, "ProvidersList");
                if (providersList != null)
                {
                    lastChildCount = providersList.Children.Count;
                    
                    // Check if we have actual provider content (not just "No data" message)
                    var hasProviderContent = providersList.Children
                        .Cast<UIElement>()
                        .Any(child => child is FrameworkElement fe && 
                                     (fe.Name.Contains("Provider") || 
                                      fe.GetType().Name.Contains("Expander") ||
                                      fe.GetType().Name.Contains("Border")));
                    
                    if (hasProviderContent)
                    {
                        providersFound = true;
                        break;
                    }
                }
                
                await Task.Delay(500);
                DoEvents();
            }
            
            mainWindow.Close();
            
            return new
            {
                ProvidersFound = providersFound,
                ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                LastChildCount = lastChildCount
            };
        });
        
        Assert.True(testResult.ProvidersFound,
            $"Providers should appear in UI within 15 seconds. " +
            $"Elapsed: {testResult.ElapsedSeconds:F1}s, " +
            $"ProvidersList children: {testResult.LastChildCount}");
    }

    [Fact]
    public async Task Monitor_API_Returns_Providers()
    {
        // Verify the Monitor's /api/usage endpoint returns data
        // This isolates whether the issue is in Monitor or UI
        
        using var client = new HttpClient();
        var response = await client.GetAsync("http://localhost:5000/api/usage");
        
        Assert.True(response.IsSuccessStatusCode, 
            $"Monitor API returned {response.StatusCode}");
        
        var providers = await response.Content.ReadFromJsonAsync<List<ProviderUsage>>();
        
        Assert.NotNull(providers);
        Assert.True(providers.Count > 0,
            $"Monitor API returned {providers.Count} providers. " +
            $"Expected > 0. If 0, Monitor has no data to serve.");
        
        // Log what we got
        Debug.WriteLine($"Monitor returned {providers.Count} providers:");
        foreach (var p in providers)
        {
            Debug.WriteLine($"  - {p.ProviderId}: {p.ProviderName} (Available: {p.IsAvailable})");
        }
    }

    [Fact]
    public async Task UI_Displays_What_Monitor_Provides()
    {
        // End-to-end test: Compare Monitor API output with UI display
        
        // Step 1: Get data from Monitor
        using var client = new HttpClient();
        var monitorData = await client.GetFromJsonAsync<List<ProviderUsage>>("http://localhost:5000/api/usage");
        Assert.NotNull(monitorData);
        
        var expectedProviderIds = monitorData.Select(p => p.ProviderId).OrderBy(id => id).ToList();
        
        // Step 2: Start UI and check what it displays
        var uiProviderIds = await RunInStaAsync(async () =>
        {
            EnsureAppCreated();
            var mainWindow = new MainWindow(skipUiInitialization: false);
            mainWindow.Show();
            
            // Wait for data to appear
            await Task.Delay(TimeSpan.FromSeconds(10));
            
            // Extract provider IDs from UI
            var providersList = FindVisualChild<StackPanel>(mainWindow, "ProvidersList");
            var ids = new List<string>();
            
            if (providersList != null)
            {
                foreach (var child in providersList.Children)
                {
                    // Try to find provider ID in the UI element
                    var providerId = ExtractProviderIdFromElement(child);
                    if (!string.IsNullOrEmpty(providerId))
                    {
                        ids.Add(providerId);
                    }
                }
            }
            
            mainWindow.Close();
            return ids.OrderBy(id => id).ToList();
        });
        
        // Step 3: Compare
        Assert.Equal(expectedProviderIds, uiProviderIds);
    }

    #region Helper Methods

    private static T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        if (parent == null) return null;

        int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            
            if (child is T typedChild && typedChild.Name == name)
                return typedChild;
            
            var result = FindVisualChild<T>(child, name);
            if (result != null) return result;
        }
        
        return null;
    }

    private static string ExtractProviderIdFromElement(object element)
    {
        // Try to extract provider ID from various UI element types
        if (element is FrameworkElement fe)
        {
            // Check if it's a provider card/expander
            if (fe.Name.StartsWith("Provider") || fe.Tag?.ToString()?.StartsWith("Provider") == true)
            {
                return fe.Tag?.ToString() ?? fe.Name;
            }
        }
        return null;
    }

    private static void DoEvents()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static App EnsureAppCreated()
    {
        if (Application.Current == null)
        {
            var app = new App();
            app.InitializeComponent();
            return app;
        }
        return (App)Application.Current;
    }

    private static Task<T> RunInStaAsync<T>(Func<Task<T>> action)
    {
        var tcs = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try
            {
                action().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        tcs.SetException(t.Exception.InnerExceptions);
                    else if (t.IsCanceled)
                        tcs.SetCanceled();
                    else
                        tcs.SetResult(t.Result);
                }, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    #endregion
}
