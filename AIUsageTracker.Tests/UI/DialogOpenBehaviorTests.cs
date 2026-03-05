using System.Reflection;
using System.Threading;
using System.Windows;
using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public class DialogOpenBehaviorTests
{
    [Fact]
    public Task OpenSettingsDialogAsync_ShowsOwnedDialog_WithoutTopmostToggle()
    {
        return RunInStaAsync(async () =>
        {
            EnsureAppCreated();

            var mainWindow = new MainWindow(skipUiInitialization: true);
            var dialogWindow = new Window();
            var shown = 0;

            mainWindow.Show();

            SetPrivateField(mainWindow, "_preferences", new AppPreferences { AlwaysOnTop = true });
            mainWindow.Topmost = true;
            mainWindow.SettingsDialogFactory = () => (dialogWindow, () => false);
            mainWindow.ShowOwnedDialog = dialog =>
            {
                shown++;
                Assert.True(mainWindow.Topmost);
                return true;
            };

            await mainWindow.OpenSettingsDialogAsync();

            Assert.Equal(1, shown);
            Assert.True(mainWindow.Topmost);

            dialogWindow.Close();
            mainWindow.Close();
        });
    }

    [Fact]
    public Task OpenInfoDialog_UsesConfiguredDialogHost_WhenMainWindowNotVisible()
    {
        return RunInStaAsync(() =>
        {
            var app = EnsureAppCreated();
            var mainWindow = new MainWindow(skipUiInitialization: true);
            var infoDialog = new Window();
            var shown = 0;

            app.SetMainWindowForTesting(mainWindow);
            app.IsMainWindowVisible = () => false;
            app.InfoDialogFactory = () => infoDialog;
            app.ShowInfoDialogAction = _ => shown++;

            app.OpenInfoDialog();

            Assert.Equal(1, shown);
            Assert.Null(infoDialog.Owner);

            infoDialog.Close();
            mainWindow.Close();

            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task CloseSettingsDialog_DoesNotMoveWindowPosition()
    {
        return RunInStaAsync(async () =>
        {
            EnsureAppCreated();

            var mainWindow = new MainWindow(skipUiInitialization: true);
            var dialogWindow = new Window();

            mainWindow.Show();
            
            // Set initial position and preferences
            var initialLeft = 500.0;
            var initialTop = 300.0;
            mainWindow.Left = initialLeft;
            mainWindow.Top = initialTop;
            
            SetPrivateField(mainWindow, "_preferences", new AppPreferences 
            { 
                AlwaysOnTop = true,
                WindowLeft = 100.0,  // Different from current position
                WindowTop = 200.0
            });
            SetPrivateField(mainWindow, "_preferencesLoaded", true);
            
            mainWindow.SettingsDialogFactory = () => (dialogWindow, () => false);
            mainWindow.ShowOwnedDialog = _ => true;

            // Open and close settings dialog
            await mainWindow.OpenSettingsDialogAsync();

            // Verify window position hasn't changed
            Assert.Equal(initialLeft, mainWindow.Left);
            Assert.Equal(initialTop, mainWindow.Top);

            dialogWindow.Close();
            mainWindow.Close();
        });
    }

    private static App EnsureAppCreated()
    {
        if (Application.Current is App app)
        {
            return app;
        }

        return new App();
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }

    private static Task RunInStaAsync(Func<Task> testBody)
    {
        var tcs = new TaskCompletionSource<object?>();

        var thread = new Thread(() =>
        {
            try
            {
                testBody().GetAwaiter().GetResult();
                tcs.SetResult(null);
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
