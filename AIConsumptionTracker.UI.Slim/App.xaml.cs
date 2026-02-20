using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using System.Drawing;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Core.AgentClient;
using System.Runtime.InteropServices;
using System.Linq;

namespace AIConsumptionTracker.UI.Slim;

public partial class App : Application
{
    public static AgentService AgentService { get; } = new();
    public static AppPreferences Preferences { get; set; } = new();
    public static bool IsPrivacyMode { get; set; } = false;
    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;

    public static event EventHandler<bool>? PrivacyChanged;

    public static void SetPrivacyMode(bool enabled)
    {
        IsPrivacyMode = enabled;
        Preferences.IsPrivacyMode = enabled;
        PrivacyChanged?.Invoke(null, enabled);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--debug"))
        {
            AllocConsole();
            Console.WriteLine("");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("  AIConsumptionTracker.UI - DEBUG MODE");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine($"  Started:    {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"  Process ID: {Environment.ProcessId}");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("");
            
            AgentService.LogDiagnostic("AI Consumption Tracker UI Debug Mode Enabled");
        }

        base.OnStartup(e);
        
        // Load preferences from Agent
        _ = LoadPreferencesAsync();
        
        // Create tray icon
        InitializeTrayIcon();
        
        // Create and show main window
        _mainWindow = new MainWindow();
        _mainWindow.Show();
    }

    private void InitializeTrayIcon()
    {
        // Create context menu
        var contextMenu = new ContextMenu();
        
        // Show menu item
        var showMenuItem = new MenuItem { Header = "Show" };
        showMenuItem.Click += (s, e) =>
        {
            if (_mainWindow != null)
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            }
        };
        contextMenu.Items.Add(showMenuItem);
        
        // Separator
        contextMenu.Items.Add(new Separator());
        
        // Info menu item
        var infoMenuItem = new MenuItem { Header = "Info" };
        infoMenuItem.Click += (s, e) =>
        {
            var infoDialog = new InfoDialog();
            // If main window is visible, center over it, otherwise center screen (default)
            if (_mainWindow != null && _mainWindow.IsVisible)
            {
                infoDialog.Owner = _mainWindow;
                infoDialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            infoDialog.Show();
            infoDialog.Activate();
        };
        contextMenu.Items.Add(infoMenuItem);
        
        // Separator
        contextMenu.Items.Add(new Separator());
        
        // Exit menu item
        var exitMenuItem = new MenuItem { Header = "Exit" };
        exitMenuItem.Click += (s, e) =>
        {
            Shutdown();
        };
        contextMenu.Items.Add(exitMenuItem);
        
        // Create tray icon
        _trayIcon = new TaskbarIcon
        {
            Icon = new System.Drawing.Icon("Assets/app_icon.ico"),
            ToolTipText = "AI Consumption Tracker",
            ContextMenu = contextMenu,
            DoubleClickCommand = new RelayCommand(() =>
            {
                if (_mainWindow != null)
                {
                    _mainWindow.Show();
                    _mainWindow.WindowState = WindowState.Normal;
                    _mainWindow.Activate();
                }
            })
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private static async Task LoadPreferencesAsync()
    {
        try
        {
            Preferences = await AgentService.GetPreferencesAsync();
            IsPrivacyMode = Preferences.IsPrivacyMode;
        }
        catch
        {
            // Use defaults
        }
    }
}

// Simple relay command implementation
public class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { System.Windows.Input.CommandManager.RequerySuggested += value; }
        remove { System.Windows.Input.CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();
}

public partial class App
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AllocConsole();
}
