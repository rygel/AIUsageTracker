// <copyright file="WindowBehaviorService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Service for managing window behavior, especially topmost/always-on-top functionality.
/// </summary>
public class WindowBehaviorService : IWindowBehaviorService
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);

    private readonly ILogger<WindowBehaviorService> _logger;
    private int _recoveryGeneration;

    public WindowBehaviorService(ILogger<WindowBehaviorService> logger)
    {
        this._logger = logger;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    /// <inheritdoc />
    public void EnsureTopmost(Window window)
    {
        if (!window.Topmost)
        {
            return;
        }

        // Get the window handle
        var handle = GetWindowHandle(window);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // Apply Win32 topmost
        this.ApplyWin32Topmost(handle, noActivate: false);
    }

    /// <inheritdoc />
    public void ScheduleTopmostRecovery(Window window, TimeSpan delay)
    {
        var generation = ++this._recoveryGeneration;

        var timer = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher)
        {
            Interval = delay,
        };

        timer.Tick += (s, e) =>
        {
            timer.Stop();

            // Skip if a newer recovery was scheduled
            if (generation != this._recoveryGeneration)
            {
                return;
            }

            if (window.Topmost)
            {
                this.EnsureTopmost(window);
            }
        };

        timer.Start();
    }

    /// <inheritdoc />
    public void ApplyWin32Topmost(IntPtr handle, bool noActivate, bool alwaysOnTop = true)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var flags = SwpNoSize | SwpNoMove | SwpNoOwnerZOrder;
        if (noActivate)
        {
            flags |= SwpNoActivate;
        }

        var insertAfter = alwaysOnTop ? HwndTopmost : HwndNoTopmost;
        SetWindowPos(handle, insertAfter, 0, 0, 0, 0, flags);
    }

    /// <inheritdoc />
    public string GetForegroundWindowSummary()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return "No foreground window";
        }

        var titleLength = GetWindowTextLength(foreground);
        var title = string.Empty;

        if (titleLength > 0)
        {
            var sb = new StringBuilder(titleLength + 1);
            GetWindowText(foreground, sb, sb.Capacity);
            title = sb.ToString();
        }

        GetWindowThreadProcessId(foreground, out var processId);
        string processName;

        try
        {
            var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch
        {
            processName = "Unknown";
        }

        return $"'{title}' ({processName}, PID: {processId})";
    }

    private static IntPtr GetWindowHandle(Window window)
    {
        var helper = new WindowInteropHelper(window);
        return helper.Handle;
    }
}
