// <copyright file="PrivacyButtonBehaviorTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
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

/// <summary>
/// Tests that simulate clicking the privacy button end-to-end:
/// PrivacyBtn_Click → App.SetPrivacyMode → PrivacyChanged event →
/// OnPrivacyChanged → _isPrivacyMode updated + button icon updated.
/// </summary>
public class PrivacyButtonBehaviorTests
{
    private static readonly TimeSpan StaTestTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public Task Diagnostic_OnPrivacyChanged_UpdatesIsPrivacyMode_WhenCalledDirectly()
    {
        return RunInStaAsync(() =>
        {
            EnsureAppCreated();
            var originalPrivacyMode = App.IsPrivacyMode;

            try
            {
                var mainWindow = CreateMainWindowForTesting();
                SetPrivateField(mainWindow, "PrivacyBtn", new Button());
                SetPrivateField(mainWindow, "_preferences", new AppPreferences { IsPrivacyMode = false });
                SetPrivateField(mainWindow, "_isPrivacyMode", false);

                // Directly invoke OnPrivacyChanged — bypasses PrivacyBtn_Click entirely
                var args = new PrivacyChangedEventArgs(true);
                InvokePrivateMethod(mainWindow, "OnPrivacyChanged", new object?[] { null, args });

                // If Dispatcher.CheckAccess() returned false inside OnPrivacyChanged,
                // _isPrivacyMode won't have updated yet (BeginInvoke deferred it).
                // This test will catch THAT scenario.
                Assert.True((bool)GetPrivateField(mainWindow, "_isPrivacyMode")!,
                    "OnPrivacyChanged did not synchronously update _isPrivacyMode. " +
                    "Dispatcher.CheckAccess() likely returned false on this thread.");

                mainWindow.Close();
            }
            finally
            {
                App.SetPrivacyMode(originalPrivacyMode);
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task PrivacyBtn_Click_TogglesPrivacyModeOn_WhenCurrentlyOff()
    {
        return RunInStaAsync(() =>
        {
            EnsureAppCreated();
            var originalPrivacyMode = App.IsPrivacyMode;

            try
            {
                var mainWindow = CreateMainWindowForTesting();
                var privacyBtn = new Button();
                SetPrivateField(mainWindow, "PrivacyBtn", privacyBtn);

                // skipUiInitialization bypasses AddHandler in ctor — register manually.
                var privacyHandler = RegisterPrivacyHandler(mainWindow);

                var preferences = new AppPreferences { IsPrivacyMode = false };
                App.Preferences = preferences;
                SetPrivateField(mainWindow, "_preferences", preferences);
                SetPrivateField(mainWindow, "_isPrivacyMode", false);
                App.SetPrivacyMode(false);

                InvokePrivateMethod(mainWindow, "PrivacyBtn_Click", new object[] { mainWindow, new RoutedEventArgs() });

                Assert.True((bool)GetPrivateField(mainWindow, "_isPrivacyMode")!);
                Assert.True(App.IsPrivacyMode);
                Assert.True(App.Preferences.IsPrivacyMode);
                Assert.Equal("\uE72E", privacyBtn.Content);

                PrivacyChangedWeakEventManager.RemoveHandler(privacyHandler);
                mainWindow.Close();
            }
            finally
            {
                App.SetPrivacyMode(originalPrivacyMode);
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task PrivacyBtn_Click_TogglesPrivacyModeOff_WhenCurrentlyOn()
    {
        return RunInStaAsync(() =>
        {
            EnsureAppCreated();
            var originalPrivacyMode = App.IsPrivacyMode;

            try
            {
                var mainWindow = CreateMainWindowForTesting();
                var privacyBtn = new Button();
                SetPrivateField(mainWindow, "PrivacyBtn", privacyBtn);

                // skipUiInitialization bypasses AddHandler in ctor — register manually.
                var privacyHandler = RegisterPrivacyHandler(mainWindow);

                var preferences = new AppPreferences { IsPrivacyMode = true };
                App.Preferences = preferences;
                SetPrivateField(mainWindow, "_preferences", preferences);
                SetPrivateField(mainWindow, "_isPrivacyMode", true);
                App.SetPrivacyMode(true);

                InvokePrivateMethod(mainWindow, "PrivacyBtn_Click", new object[] { mainWindow, new RoutedEventArgs() });

                Assert.False((bool)GetPrivateField(mainWindow, "_isPrivacyMode")!);
                Assert.False(App.IsPrivacyMode);
                Assert.False(App.Preferences.IsPrivacyMode);
                Assert.Equal("\uE785", privacyBtn.Content);

                PrivacyChangedWeakEventManager.RemoveHandler(privacyHandler);
                mainWindow.Close();
            }
            finally
            {
                App.SetPrivacyMode(originalPrivacyMode);
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task PrivacyBtn_Click_TogglesCorrectly_OnMultipleConsecutiveClicks()
    {
        return RunInStaAsync(() =>
        {
            EnsureAppCreated();
            var originalPrivacyMode = App.IsPrivacyMode;

            try
            {
                var mainWindow = CreateMainWindowForTesting();
                var privacyBtn = new Button();
                SetPrivateField(mainWindow, "PrivacyBtn", privacyBtn);

                // skipUiInitialization bypasses AddHandler in ctor — register manually.
                var privacyHandler = RegisterPrivacyHandler(mainWindow);

                var preferences = new AppPreferences { IsPrivacyMode = false };
                App.Preferences = preferences;
                SetPrivateField(mainWindow, "_preferences", preferences);
                SetPrivateField(mainWindow, "_isPrivacyMode", false);
                App.SetPrivacyMode(false);

                // Click 1: off → on
                InvokePrivateMethod(mainWindow, "PrivacyBtn_Click", new object[] { mainWindow, new RoutedEventArgs() });
                Assert.True((bool)GetPrivateField(mainWindow, "_isPrivacyMode")!);
                Assert.Equal("\uE72E", privacyBtn.Content);

                // Click 2: on → off
                InvokePrivateMethod(mainWindow, "PrivacyBtn_Click", new object[] { mainWindow, new RoutedEventArgs() });
                Assert.False((bool)GetPrivateField(mainWindow, "_isPrivacyMode")!);
                Assert.Equal("\uE785", privacyBtn.Content);

                // Click 3: off → on again
                InvokePrivateMethod(mainWindow, "PrivacyBtn_Click", new object[] { mainWindow, new RoutedEventArgs() });
                Assert.True((bool)GetPrivateField(mainWindow, "_isPrivacyMode")!);
                Assert.Equal("\uE72E", privacyBtn.Content);

                PrivacyChangedWeakEventManager.RemoveHandler(privacyHandler);
                mainWindow.Close();
            }
            finally
            {
                App.SetPrivacyMode(originalPrivacyMode);
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task PrivacyBtn_Click_FiresPrivacyChangedEvent()
    {
        return RunInStaAsync(() =>
        {
            EnsureAppCreated();
            var originalPrivacyMode = App.IsPrivacyMode;

            try
            {
                var mainWindow = CreateMainWindowForTesting();
                SetPrivateField(mainWindow, "PrivacyBtn", new Button());

                var privacyHandler = RegisterPrivacyHandler(mainWindow);

                var preferences = new AppPreferences { IsPrivacyMode = false };
                App.Preferences = preferences;
                SetPrivateField(mainWindow, "_preferences", preferences);
                SetPrivateField(mainWindow, "_isPrivacyMode", false);
                App.SetPrivacyMode(false);

                // Subscribe after setup so the handler only sees the click-triggered event.
                var eventFiredCount = 0;
                var externalHandler = new EventHandler<PrivacyChangedEventArgs>((_, e) =>
                {
                    eventFiredCount++;
                    Assert.True(e.IsPrivacyMode);
                });
                App.PrivacyChanged += externalHandler;

                try
                {
                    InvokePrivateMethod(mainWindow, "PrivacyBtn_Click", new object[] { mainWindow, new RoutedEventArgs() });
                    Assert.Equal(1, eventFiredCount);
                }
                finally
                {
                    App.PrivacyChanged -= externalHandler;
                }

                PrivacyChangedWeakEventManager.RemoveHandler(privacyHandler);
                mainWindow.Close();
            }
            finally
            {
                App.SetPrivacyMode(originalPrivacyMode);
            }

            return Task.CompletedTask;
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

    private static MainWindow CreateMainWindowForTesting()
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
            services.GetRequiredService<IDialogService>(),
            services.GetRequiredService<IBrowserService>(),
            services.GetRequiredService<UiPreferencesStore>());
    }

    /// <summary>
    /// When skipUiInitialization is true the constructor returns early, skipping the
    /// PrivacyChangedWeakEventManager.AddHandler call. Register the handler manually so
    /// the App.PrivacyChanged → OnPrivacyChanged chain works during tests.
    /// </summary>
    private static EventHandler<PrivacyChangedEventArgs> RegisterPrivacyHandler(MainWindow mainWindow)
    {
        var handler = (EventHandler<PrivacyChangedEventArgs>)GetPrivateField(mainWindow, "_privacyChangedHandler")!;
        PrivacyChangedWeakEventManager.AddHandler(handler);
        return handler;
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
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

    private static void InvokePrivateMethod(object target, string methodName, object[]? parameters = null)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(target, parameters);
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
