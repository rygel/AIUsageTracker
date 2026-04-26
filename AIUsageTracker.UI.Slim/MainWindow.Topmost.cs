// <copyright file="MainWindow.Topmost.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

/// <summary>
/// Partial class containing topmost/always-on-top window management and Win32 interop.
/// </summary>
public partial class MainWindow
{
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        this._windowSource = PresentationSource.FromVisual(this) as HwndSource;
        this._windowSource?.AddHook(this.WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_ACTIVATEAPP = 0x001C;

        if (msg == WM_ACTIVATEAPP)
        {
            var isActive = wParam != IntPtr.Zero;
            this.LogWindowFocusTransition($"WM_ACTIVATEAPP -> {(isActive ? "active" : "inactive")}");
        }

        return IntPtr.Zero;
    }

    private void LogWindowFocusTransition(string eventName)
    {
        var foregroundSummary = GetForegroundWindowSummary();
        var message = $"[WINDOW] evt={eventName} fg={foregroundSummary} vis={this.IsVisible} state={this.WindowState} top={this.Topmost}";
        this._logger.LogDebug("{WindowMessage}", message);
    }

    private static string GetForegroundWindowSummary()
    {
        var hwnd = Win32Interop.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return "none";
        }

        var titleLength = Win32Interop.GetWindowTextLength(hwnd);
        var builder = new StringBuilder(Math.Max(titleLength + 1, 1));
        _ = Win32Interop.GetWindowText(hwnd, builder, builder.Capacity);

        _ = Win32Interop.GetWindowThreadProcessId(hwnd, out var processId);
        var processName = "unknown";
        if (processId > 0)
        {
            try
            {
                processName = Process.GetProcessById((int)processId).ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                processName = "unavailable";
            }
        }

        var title = builder
            .ToString()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            title = "<no-title>";
        }

        if (title.Length > 80)
        {
            title = title[..80] + "...";
        }

        return $"pid={processId} proc={processName} title={title}";
    }

    private void EnsureAlwaysOnTop()
    {
        if (this._isSettingsDialogOpen || this._isChangelogOpen || this._isTooltipOpen || !this._preferences.AlwaysOnTop || !this.IsVisible || this.WindowState == WindowState.Minimized)
        {
            return;
        }

        if (!this.Topmost)
        {
            this.Topmost = true;
        }

        this.ApplyWin32Topmost(noActivate: true);
    }

    private void ApplyTopmostState(bool alwaysOnTop)
    {
        this.Topmost = alwaysOnTop;
        this.ApplyWin32Topmost(noActivate: true, alwaysOnTop);
    }

    private void ScheduleTopmostRecovery(int generation, TimeSpan delay)
    {
#pragma warning disable VSTHRD001 // Recovery runs from a background delay and must marshal back to the UI thread explicitly.
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay).ConfigureAwait(false); // ui-thread-guardrail-allow: continuation immediately marshals via Dispatcher.
            await this.Dispatcher.InvokeAsync(
                () =>
            {
                if (generation != this._topmostRecoveryGeneration)
                {
                    return;
                }

                this.ReassertTopmostWithoutFocus();
                this.LogWindowFocusTransition($"TopmostRecovery +{delay.TotalMilliseconds:0}ms");
            }, DispatcherPriority.Normal);
        });
#pragma warning restore VSTHRD001
    }

    private void ReassertTopmostWithoutFocus()
    {
        if (this._isSettingsDialogOpen || this._isChangelogOpen || this._isTooltipOpen || !this._preferences.AlwaysOnTop || !this.IsVisible || this.WindowState == WindowState.Minimized)
        {
            return;
        }

        if (!this.Topmost)
        {
            this.Topmost = true;
            this.ApplyWin32Topmost(noActivate: true);
            return;
        }

        if (this._preferences.AggressiveAlwaysOnTop)
        {
            this.Topmost = false;
            this.Topmost = true;
        }

        this.ApplyWin32Topmost(noActivate: true);
    }

    private void ApplyWin32Topmost(bool noActivate, bool alwaysOnTop = true)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var flags = Win32Interop.SwpNoMove | Win32Interop.SwpNoSize | Win32Interop.SwpNoOwnerZOrder;
        if (noActivate)
        {
            flags |= Win32Interop.SwpNoActivate;
        }

        var insertAfter = alwaysOnTop ? Win32Interop.HwndTopmost : Win32Interop.HwndNoTopmost;
        var applied = Win32Interop.SetWindowPos(handle, insertAfter, 0, 0, 0, 0, flags);
        if (!applied)
        {
            var win32Error = Marshal.GetLastWin32Error();
            this._logger.LogWarning(
                "SetWindowPos failed err={Win32Error} alwaysOnTop={AlwaysOnTop} noActivate={NoActivate}",
                win32Error,
                alwaysOnTop,
                noActivate);
        }
    }
}
