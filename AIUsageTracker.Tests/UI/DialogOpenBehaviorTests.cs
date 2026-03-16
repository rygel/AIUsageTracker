// <copyright file="DialogOpenBehaviorTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.UI;

public class DialogOpenBehaviorTests
{
    private static readonly TimeSpan StaTestTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public Task OpenSettingsDialogAsync_ShowsOwnedDialog_WithoutTopmostToggleAsync()
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

            await mainWindow.OpenSettingsDialogAsync().ConfigureAwait(false);

            Assert.Equal(1, shown);
            Assert.True(mainWindow.Topmost);

            dialogWindow.Close();
            mainWindow.Close();
        });
    }

    [Fact]
    public Task OpenInfoDialog_UsesConfiguredDialogHost_WhenMainWindowNotVisibleAsync()
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
    public Task CloseSettingsDialog_DoesNotMoveWindowPositionAsync()
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
                WindowTop = 200.0,
            });
            SetPrivateField(mainWindow, "_preferencesLoaded", true);

            mainWindow.SettingsDialogFactory = () => (dialogWindow, () => false);
            mainWindow.ShowOwnedDialog = _ => true;

            // Open and close settings dialog
            await mainWindow.OpenSettingsDialogAsync().ConfigureAwait(false);

            // Verify window position hasn't changed
            Assert.Equal(initialLeft, mainWindow.Left);
            Assert.Equal(initialTop, mainWindow.Top);

            dialogWindow.Close();
            mainWindow.Close();
        });
    }

    [Fact]
    public Task MainWindowAndSettingsWindow_ReflectSameShowUsedPreferenceAsync()
    {
        return RunInStaAsync(() =>
        {
            EnsureAppCreated();

            var preferences = new AppPreferences { ShowUsedPercentages = true };
            var mainWindow = new MainWindow(skipUiInitialization: true);
            var settingsWindow = (SettingsWindow)FormatterServices.GetUninitializedObject(typeof(SettingsWindow));
            var displayPreferences = new DisplayPreferencesService();

            SetPrivateField(mainWindow, "_preferences", preferences);
            SetPrivateField(settingsWindow, "_preferences", preferences);
            SetPrivateField(settingsWindow, "_displayPreferences", displayPreferences);
            SetPrivateField(mainWindow, "ShowUsedToggle", new CheckBox());
            SetPrivateField(settingsWindow, "ShowUsedPercentagesCheck", new CheckBox());
            InvokePrivateMethod(mainWindow, "ApplyDisplayModePreference");
            InvokePrivateMethod(settingsWindow, "ApplyDisplayModePreference");

            var mainToggle = Assert.IsType<CheckBox>(GetPrivateField(mainWindow, "ShowUsedToggle"));
            var settingsToggle = Assert.IsType<CheckBox>(GetPrivateField(settingsWindow, "ShowUsedPercentagesCheck"));

            Assert.True(mainToggle.IsChecked);
            Assert.Equal(mainToggle.IsChecked, settingsToggle.IsChecked);

            return Task.CompletedTask;
        });
    }

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

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }

    private static object? GetPrivateField(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field.GetValue(target);
    }

    private static void InvokePrivateMethod(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(target, null);
    }

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
