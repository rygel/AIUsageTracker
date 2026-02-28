using System.Diagnostics;
using System.Threading;
using System.Windows;
using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public class StartupTimingTests
{
    [Fact]
    public Task MainWindow_ShouldInitializeWithin10Seconds()
    {
        return RunInStaAsync(async () =>
        {
            EnsureAppCreated();

            var mainWindow = new MainWindow(skipUiInitialization: false);
            var stopwatch = Stopwatch.StartNew();
            
            mainWindow.Show();
            
            // Wait for window to be loaded and initialized
            var loadedTcs = new TaskCompletionSource<bool>();
            mainWindow.Loaded += (s, e) => loadedTcs.TrySetResult(true);
            
            // Timeout after 10 seconds
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
            var completedTask = await Task.WhenAny(loadedTcs.Task, timeoutTask);
            
            stopwatch.Stop();
            
            Assert.True(completedTask == loadedTcs.Task, 
                $"Window initialization timed out after {stopwatch.Elapsed.TotalSeconds:F1}s (max 10s)");
            Assert.True(stopwatch.Elapsed.TotalSeconds < 10, 
                $"Startup took {stopwatch.Elapsed.TotalSeconds:F1}s, expected < 10s");
            
            mainWindow.Close();
        });
    }

    [Fact]
    public Task MainWindow_ShouldShowUI_BeforeDataIsAvailable()
    {
        return RunInStaAsync(async () =>
        {
            EnsureAppCreated();

            var mainWindow = new MainWindow(skipUiInitialization: false);
            var uiShown = false;
            
            // Track when UI is rendered
            mainWindow.ContentRendered += (s, e) => uiShown = true;
            
            mainWindow.Show();
            
            // Wait up to 5 seconds for UI to render
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
            while (!uiShown && timeoutTask.Status != TaskStatus.RanToCompletion)
            {
                await Task.Delay(100);
                // Pump messages
                DoEvents();
            }
            
            Assert.True(uiShown, "UI should be shown within 5 seconds, even without data");
            
            mainWindow.Close();
        });
    }

    [Fact]
    public Task MainWindow_ShouldNotBlockUIThread()
    {
        return RunInStaAsync(async () =>
        {
            EnsureAppCreated();

            var mainWindow = new MainWindow(skipUiInitialization: false);
            var uiThreadResponsive = true;
            
            mainWindow.Show();
            
            // Wait a bit for initialization to start
            await Task.Delay(500);
            
            // Try to interact with UI during initialization
            try
            {
                // This should not block - if UI thread is frozen, this will timeout
                await mainWindow.Dispatcher.InvokeAsync(() =>
                {
                    var title = mainWindow.Title;
                    return title;
                }, System.Windows.Threading.DispatcherPriority.Normal).Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
                uiThreadResponsive = false;
            }
            
            Assert.True(uiThreadResponsive, "UI thread should remain responsive during initialization");
            
            mainWindow.Close();
        });
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

    private static void SetPrivateField(object obj, string fieldName, object value)
    {
        var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(obj, value);
    }

    private static Task RunInStaAsync(Func<Task> action)
    {
        var tcs = new TaskCompletionSource<object>();
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
                        tcs.SetResult(null);
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
}
