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
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.UI.Slim;
using AIUsageTracker.UI.Slim.Services;
using AIUsageTracker.UI.Slim.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

            var dialogService = new TestDialogService();
            var mainWindow = CreateMainWindowForTesting(dialogService);
            var shown = 0;

            mainWindow.Show();

            SetPrivateField(mainWindow, "_preferences", new AppPreferences { AlwaysOnTop = true });
            mainWindow.Topmost = true;
            dialogService.ShowSettingsAsyncHandler = owner =>
            {
                shown++;
                Assert.Same(mainWindow, owner);
                Assert.True(mainWindow.Topmost);
                return Task.FromResult<bool?>(false);
            };

            await mainWindow.OpenSettingsDialogAsync().ConfigureAwait(false);

            Assert.Equal(1, shown);
            Assert.True(mainWindow.Topmost);

            mainWindow.Close();
        });
    }

    [Fact]
    public Task OpenInfoDialog_UsesConfiguredDialogHost_WhenMainWindowNotVisibleAsync()
    {
        return RunInStaAsync(() =>
        {
            var app = EnsureAppCreated();
            var mainWindow = CreateMainWindowForTesting();
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

            var dialogService = new TestDialogService
            {
                ShowSettingsAsyncHandler = _ => Task.FromResult<bool?>(false),
            };
            var mainWindow = CreateMainWindowForTesting(dialogService);

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

            // Open and close settings dialog
            await mainWindow.OpenSettingsDialogAsync().ConfigureAwait(false);

            // Verify window position hasn't changed
            Assert.Equal(initialLeft, mainWindow.Left);
            Assert.Equal(initialTop, mainWindow.Top);

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
            var mainWindow = CreateMainWindowForTesting();
#pragma warning disable SYSLIB0050
            var settingsWindow = (SettingsWindow)FormatterServices.GetUninitializedObject(typeof(SettingsWindow));
#pragma warning restore SYSLIB0050

            SetPrivateField(mainWindow, "_preferences", preferences);
            SetPrivateField(settingsWindow, "_preferences", preferences);
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

    [Fact]
    public Task SettingsWindow_DisplayControlsApplyToPreferencesThroughSinglePathAsync()
    {
        return RunInStaAsync(() =>
        {
            EnsureAppCreated();

            var preferences = new AppPreferences
            {
                ShowUsedPercentages = false,
                ShowUsagePerHour = false,
                ShowDualQuotaBars = true,
                DualQuotaSingleBarMode = DualQuotaSingleBarMode.Rolling,
                EnablePaceAdjustment = true,
                UseRelativeResetTime = false,
                ColorThresholdYellow = 60,
                ColorThresholdRed = 80,
            };
#pragma warning disable SYSLIB0050
            var settingsWindow = (SettingsWindow)FormatterServices.GetUninitializedObject(typeof(SettingsWindow));
#pragma warning restore SYSLIB0050
            var dualModeCombo = new ComboBox
            {
                ItemsSource = new[]
                {
                    new { Value = DualQuotaSingleBarMode.Rolling },
                    new { Value = DualQuotaSingleBarMode.Burst },
                },
                SelectedValuePath = "Value",
                SelectedIndex = 1,
            };

            SetPrivateField(settingsWindow, "_preferences", preferences);
            SetPrivateField(settingsWindow, "ShowUsedPercentagesCheck", new CheckBox { IsChecked = true });
            SetPrivateField(settingsWindow, "ShowUsagePerHourCheck", new CheckBox { IsChecked = true });
            SetPrivateField(settingsWindow, "ShowDualQuotaBarsCheck", new CheckBox { IsChecked = false });
            SetPrivateField(settingsWindow, "DualQuotaBarWindowCombo", dualModeCombo);
            SetPrivateField(settingsWindow, "EnablePaceAdjustmentCheck", new CheckBox { IsChecked = false });
            SetPrivateField(settingsWindow, "UseRelativeResetTimeCheck", new CheckBox { IsChecked = true });
            SetPrivateField(settingsWindow, "YellowThreshold", new TextBox { Text = "55" });
            SetPrivateField(settingsWindow, "RedThreshold", new TextBox { Text = "85" });

            InvokePrivateMethod(settingsWindow, "ApplyDisplayPreferencesFromControls");

            Assert.True(preferences.ShowUsedPercentages);
            Assert.True(preferences.ShowUsagePerHour);
            Assert.False(preferences.ShowDualQuotaBars);
            Assert.Equal(DualQuotaSingleBarMode.Burst, preferences.DualQuotaSingleBarMode);
            Assert.False(preferences.EnablePaceAdjustment);
            Assert.True(preferences.UseRelativeResetTime);
            Assert.Equal(55, preferences.ColorThresholdYellow);
            Assert.Equal(85, preferences.ColorThresholdRed);

            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task OpenSettingsDialogAsync_WhenSettingsChanged_ReloadsEnablePaceAdjustmentFromStoreAsync()
    {
        return RunInStaAsync(async () =>
        {
            EnsureAppCreated();

            var dialogService = new TestDialogService
            {
                ShowSettingsAsyncHandler = _ => Task.FromResult<bool?>(true),
            };
            var mainWindow = CreateMainWindowForTesting(dialogService);
            var preferencesStore = Assert.IsType<UiPreferencesStore>(GetPrivateField(mainWindow, "_preferencesStore"));
            var pathOverrideMethod = typeof(UiPreferencesStore).GetMethod(
                "SetPreferencesPathOverrideForTesting",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(pathOverrideMethod);

            var tempPreferencesPath = Path.Combine(
                Path.GetTempPath(),
                $"aiusagetracker-prefs-{Guid.NewGuid():N}.json");

            pathOverrideMethod.Invoke(preferencesStore, new object?[] { tempPreferencesPath });

            try
            {
                var persisted = new AppPreferences
                {
                    EnablePaceAdjustment = false,
                    ShowUsedPercentages = true,
                };
                await preferencesStore.SaveAsync(persisted).ConfigureAwait(false);

                SetPrivateField(mainWindow, "_preferences", new AppPreferences
                {
                    EnablePaceAdjustment = true,
                    ShowUsedPercentages = true,
                });
                SetPrivateField(mainWindow, "_preferencesLoaded", true);

                // Simulate the settings dialog updating App.Preferences
                // (in production, SettingsWindow sets App.Preferences = this._preferences)
                App.Preferences = persisted;

                // Avoid monitor startup work in this unit test; we only need the settings-change path.
                SetPrivateField(mainWindow, "_isLoading", true);

                await mainWindow.OpenSettingsDialogAsync().ConfigureAwait(false);

                var reloaded = Assert.IsType<AppPreferences>(GetPrivateField(mainWindow, "_preferences"));
                Assert.False(reloaded.EnablePaceAdjustment);
            }
            finally
            {
                if (File.Exists(tempPreferencesPath))
                {
                    File.Delete(tempPreferencesPath);
                }

                if (mainWindow.Dispatcher.CheckAccess())
                {
                    mainWindow.Close();
                }
            }
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

    private static MainWindow CreateMainWindowForTesting(IDialogService? dialogService = null, IBrowserService? browserService = null)
    {
        EnsureAppCreated();
        var services = App.Host.Services;

        return new MainWindow(
            skipUiInitialization: true,
            services.GetRequiredService<MainViewModel>(),
            services.GetRequiredService<IMonitorService>(),
            services.GetRequiredService<MonitorLifecycleService>(),
            services.GetRequiredService<MonitorStartupOrchestrator>(),
            services.GetRequiredService<ILogger<MainWindow>>(),
            services.GetRequiredService<Func<UpdateChannel, GitHubUpdateChecker>>(),
            services.GetRequiredService<GitHubUpdateChecker>(),
            dialogService ?? services.GetRequiredService<IDialogService>(),
            browserService ?? services.GetRequiredService<IBrowserService>(),
            services.GetRequiredService<UiPreferencesStore>());
    }

    private sealed class TestDialogService : IDialogService
    {
        public Func<Window?, Task<bool?>> ShowSettingsAsyncHandler { get; set; } = _ => Task.FromResult<bool?>(false);

        public Task<bool?> ShowSettingsAsync(Window? owner = null)
        {
            return this.ShowSettingsAsyncHandler(owner);
        }

        public Task ShowInfoAsync(Window? owner = null)
        {
            return Task.CompletedTask;
        }

        public Task<string?> ShowSaveFileDialogAsync(string filter, string defaultFileName)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> ShowOpenFileDialogAsync(string filter)
        {
            return Task.FromResult<string?>(null);
        }
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
