// <copyright file="DialogOpenBehaviorTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.UI
{
    using System.Reflection;
    using System.Threading;
    using System.Windows;
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.UI.Slim;

    public class DialogOpenBehaviorTests
    {
        private static readonly TimeSpan StaTestTimeout = TimeSpan.FromSeconds(15);

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
    `n
        private static App EnsureAppCreated()
        {
            if (Application.Current is App app)
            {
                return app;
            }

            var newApp = new App();
            // Use reflection to initialize Host if needed, or rely on App.xaml.cs default init
            return newApp;
        }
    `n
        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(target, value);
        }
    `n
        private static Task RunInStaAsync(Func<Task> testBody)
        {
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                try
                {
                    testBody().WaitAsync(StaTestTimeout).GetAwaiter().GetResult();
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
}
