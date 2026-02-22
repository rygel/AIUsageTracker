using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.AgentClient;
using Microsoft.Win32;

namespace AIUsageTracker.UI.Slim;

public partial class SettingsWindow : Window
{
    private readonly AgentService _agentService;
    private List<ProviderConfig> _configs = new();
    private List<ProviderUsage> _usages = new();
    private AppPreferences _preferences = new();
    private bool _isPrivacyMode = App.IsPrivacyMode;
    private bool _isDeterministicScreenshotMode;

    public bool SettingsChanged { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
        _agentService = new AgentService();
        App.PrivacyChanged += OnPrivacyChanged;
        Closed += SettingsWindow_Closed;
        Loaded += SettingsWindow_Loaded;
        UpdatePrivacyButtonState();
    }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _isDeterministicScreenshotMode = false;
        _configs = await _agentService.GetConfigsAsync();
        _usages = await _agentService.GetUsageAsync();
        _preferences = await UiPreferencesStore.LoadAsync();
        App.Preferences = _preferences;
        _isPrivacyMode = _preferences.IsPrivacyMode;
        App.SetPrivacyMode(_isPrivacyMode);
        UpdatePrivacyButtonState();

        PopulateProviders();
        RefreshTrayIcons();
        PopulateLayoutSettings();
        await LoadHistoryAsync();
        await UpdateAgentStatusAsync();
        RefreshDiagnosticsLog();
    }

    internal async Task PrepareForHeadlessScreenshotAsync(bool deterministic = false)
    {
        if (deterministic)
        {
            PrepareDeterministicScreenshotData();
        }
        else
        {
            await LoadDataAsync();
        }

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        UpdateLayout();
    }

    internal async Task<IReadOnlyList<string>> CaptureHeadlessTabScreenshotsAsync(string outputDirectory)
    {
        await PrepareForHeadlessScreenshotAsync(deterministic: true);

        var capturedFiles = new List<string>();
        if (MainTabControl.Items.Count == 0)
        {
            const string fallbackName = "screenshot_settings_privacy.png";
            App.RenderWindowContent(this, Path.Combine(outputDirectory, fallbackName));
            capturedFiles.Add(fallbackName);
            return capturedFiles;
        }

        for (var index = 0; index < MainTabControl.Items.Count; index++)
        {
            MainTabControl.SelectedIndex = index;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

            var header = (MainTabControl.Items[index] as TabItem)?.Header?.ToString();
            ApplyHeadlessCaptureWindowSize(header);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            UpdateLayout();

            var tabSlug = BuildTabSlug(header, index);
            var fileName = $"screenshot_settings_{tabSlug}_privacy.png";
            App.RenderWindowContent(this, Path.Combine(outputDirectory, fileName));
            capturedFiles.Add(fileName);
        }

        MainTabControl.SelectedIndex = 0;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        UpdateLayout();

        const string legacyName = "screenshot_settings_privacy.png";
        App.RenderWindowContent(this, Path.Combine(outputDirectory, legacyName));
        capturedFiles.Add(legacyName);

        return capturedFiles;
    }

    private void ApplyHeadlessCaptureWindowSize(string? tabHeader)
    {
        Width = 600;
        Height = 600;

        if (!_isDeterministicScreenshotMode)
        {
            return;
        }

        if (!string.Equals(tabHeader, "Providers", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Width = 760;
        ProvidersStack.Measure(new Size(Width - 80, double.PositiveInfinity));
        var desiredContentHeight = ProvidersStack.DesiredSize.Height;
        Height = Math.Max(900, Math.Min(3200, desiredContentHeight + 260));
    }

    private void PrepareDeterministicScreenshotData()
    {
        _isDeterministicScreenshotMode = true;
        _preferences = new AppPreferences
        {
            AlwaysOnTop = true,
            InvertProgressBar = true,
            InvertCalculations = false,
            ColorThresholdYellow = 60,
            ColorThresholdRed = 80,
            FontFamily = "Segoe UI",
            FontSize = 12,
            FontBold = false,
            FontItalic = false,
            IsPrivacyMode = true
        };

        App.Preferences = _preferences;
        _isPrivacyMode = true;
        App.SetPrivacyMode(true);
        UpdatePrivacyButtonState();

        ProviderConfig CreateConfig(
            string providerId,
            string apiKey,
            PlanType planType,
            string type,
            bool showInTray = false,
            bool enableNotifications = false)
        {
            return new ProviderConfig
            {
                ProviderId = providerId,
                ApiKey = apiKey,
                ShowInTray = showInTray,
                EnableNotifications = enableNotifications,
                PlanType = planType,
                Type = type
            };
        }

        _configs = new List<ProviderConfig>
        {
            CreateConfig("antigravity", "local-session", PlanType.Coding, "quota-based"),
            CreateConfig("anthropic", "sk-ant-demo", PlanType.Usage, "pay-as-you-go", showInTray: true),
            CreateConfig("claude-code", "cc-demo-key", PlanType.Usage, "pay-as-you-go"),
            CreateConfig("codex", "codex-demo-key", PlanType.Coding, "quota-based", showInTray: true),
            CreateConfig("deepseek", "sk-ds-demo", PlanType.Usage, "pay-as-you-go"),
            CreateConfig("gemini-cli", "gemini-local-auth", PlanType.Coding, "quota-based"),
            CreateConfig("github-copilot", "ghp_demo_key", PlanType.Coding, "quota-based", showInTray: true, enableNotifications: true),
            CreateConfig("kimi", "kimi-demo-key", PlanType.Coding, "quota-based"),
            CreateConfig("minimax", "mm-cn-demo", PlanType.Coding, "quota-based"),
            CreateConfig("minimax-io", "mm-intl-demo", PlanType.Usage, "pay-as-you-go"),
            CreateConfig("mistral", "mistral-demo-key", PlanType.Usage, "pay-as-you-go"),
            CreateConfig("openai", "sk-openai-demo", PlanType.Usage, "pay-as-you-go", showInTray: true),
            CreateConfig("opencode", "oc-demo-key", PlanType.Usage, "pay-as-you-go"),
            CreateConfig("opencode-zen", "ocz-demo-key", PlanType.Usage, "pay-as-you-go"),
            CreateConfig("openrouter", "or-demo-key", PlanType.Usage, "pay-as-you-go"),
            CreateConfig("synthetic", "syn-demo-key", PlanType.Coding, "quota-based"),
            CreateConfig("zai-coding-plan", "zai-demo-key", PlanType.Coding, "quota-based", showInTray: true)
        };

        var deterministicNow = DateTime.Now;
        _usages = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "antigravity",
                ProviderName = "Antigravity",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                RequestsPercentage = 60.0,
                Description = "60.0% Remaining",
                Details = new List<ProviderUsageDetail>
                {
                    new()
                    {
                        Name = "Claude Opus 4.6 (Thinking)",
                        ModelName = "Claude Opus 4.6 (Thinking)",
                        GroupName = "Recommended Group 1",
                        Used = "60%",
                        Description = "60% remaining",
                        NextResetTime = deterministicNow.AddHours(10)
                    },
                    new()
                    {
                        Name = "Claude Sonnet 4.6 (Thinking)",
                        ModelName = "Claude Sonnet 4.6 (Thinking)",
                        GroupName = "Recommended Group 1",
                        Used = "60%",
                        Description = "60% remaining",
                        NextResetTime = deterministicNow.AddHours(10)
                    },
                    new()
                    {
                        Name = "Gemini 3 Flash",
                        ModelName = "Gemini 3 Flash",
                        GroupName = "Recommended Group 1",
                        Used = "100%",
                        Description = "100% remaining",
                        NextResetTime = deterministicNow.AddHours(6)
                    },
                    new()
                    {
                        Name = "Gemini 3.1 Pro (High)",
                        ModelName = "Gemini 3.1 Pro (High)",
                        GroupName = "Recommended Group 1",
                        Used = "100%",
                        Description = "100% remaining",
                        NextResetTime = deterministicNow.AddHours(14)
                    },
                    new()
                    {
                        Name = "Gemini 3.1 Pro (Low)",
                        ModelName = "Gemini 3.1 Pro (Low)",
                        GroupName = "Recommended Group 1",
                        Used = "100%",
                        Description = "100% remaining",
                        NextResetTime = deterministicNow.AddHours(14)
                    },
                    new()
                    {
                        Name = "GPT-OSS 120B (Medium)",
                        ModelName = "GPT-OSS 120B (Medium)",
                        GroupName = "Recommended Group 1",
                        Used = "60%",
                        Description = "60% remaining",
                        NextResetTime = deterministicNow.AddHours(8)
                    }
                },
                NextResetTime = deterministicNow.AddHours(6)
            },
            new()
            {
                ProviderId = "anthropic",
                ProviderName = "Anthropic",
                IsAvailable = true,
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                RequestsPercentage = 0,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                Description = "Connected"
            },
            new()
            {
                ProviderId = "claude-code",
                ProviderName = "Claude Code",
                IsAvailable = true,
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                RequestsPercentage = 0,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                Description = "Connected"
            },
            new()
            {
                ProviderId = "codex",
                ProviderName = "Codex",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                RequestsPercentage = 63.0,
                Description = "63.0% Remaining",
                NextResetTime = deterministicNow.AddHours(18)
            },
            new()
            {
                ProviderId = "deepseek",
                ProviderName = "DeepSeek",
                IsAvailable = true,
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                RequestsPercentage = 0,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                Description = "Connected"
            },
            new()
            {
                ProviderId = "gemini-cli",
                ProviderName = "Gemini CLI",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                RequestsPercentage = 84.0,
                Description = "84.0% Remaining",
                NextResetTime = deterministicNow.AddHours(12)
            },
            new()
            {
                ProviderId = "github-copilot",
                ProviderName = "GitHub Copilot",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                RequestsPercentage = 72.5,
                Description = "72.5% Remaining",
                NextResetTime = deterministicNow.AddHours(20)
            },
            new()
            {
                ProviderId = "kimi",
                ProviderName = "Kimi",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                RequestsPercentage = 66.0,
                Description = "66.0% Remaining",
                NextResetTime = deterministicNow.AddHours(9)
            },
            new()
            {
                ProviderId = "minimax",
                ProviderName = "Minimax",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                RequestsPercentage = 61.0,
                Description = "61.0% Remaining",
                NextResetTime = deterministicNow.AddHours(11)
            },
            new()
            {
                ProviderId = "minimax-io",
                ProviderName = "Minimax International",
                IsAvailable = true,
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                RequestsPercentage = 0,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                Description = "Connected"
            },
            new()
            {
                ProviderId = "mistral",
                ProviderName = "Mistral",
                IsAvailable = true,
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                RequestsPercentage = 0,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                Description = "Connected"
            },
            new()
            {
                ProviderId = "openai",
                ProviderName = "OpenAI",
                IsAvailable = true,
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                RequestsPercentage = 0,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                Description = "Connected"
            },
            new()
            {
                ProviderId = "opencode",
                ProviderName = "OpenCode",
                IsAvailable = true,
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                RequestsPercentage = 0,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                Description = "Connected"
            },
            new()
            {
                ProviderId = "opencode-zen",
                ProviderName = "Opencode Zen",
                IsAvailable = true,
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                RequestsPercentage = 0,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                Description = "Connected"
            },
            new()
            {
                ProviderId = "openrouter",
                ProviderName = "OpenRouter",
                IsAvailable = true,
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                RequestsPercentage = 0,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                Description = "Connected"
            },
            new()
            {
                ProviderId = "synthetic",
                ProviderName = "Synthetic",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                RequestsPercentage = 79.0,
                Description = "79.0% Remaining",
                NextResetTime = deterministicNow.AddHours(4)
            },
            new()
            {
                ProviderId = "zai-coding-plan",
                ProviderName = "Z.AI",
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                RequestsPercentage = 88.0,
                Description = "88.0% Remaining",
                NextResetTime = deterministicNow.AddHours(15)
            }
        };

        PopulateProviders();
        PopulateLayoutSettings();

        HistoryDataGrid.ItemsSource = new[]
        {
            new
            {
                ProviderName = "GitHub Copilot",
                UsagePercentage = 27.5,
                Used = 27.5,
                Limit = 100.0,
                PlanType = "Coding",
                Description = "72.5% Remaining",
                FetchedAt = new DateTime(2026, 2, 1, 12, 0, 0)
            },
            new
            {
                ProviderName = "OpenAI",
                UsagePercentage = 31.1,
                Used = 12.45,
                Limit = 40.0,
                PlanType = "Usage",
                Description = "$12.45 / $40.00",
                FetchedAt = new DateTime(2026, 2, 1, 12, 5, 0)
            }
        };

        if (AgentStatusText != null)
        {
            AgentStatusText.Text = "Running";
        }

        if (AgentPortText != null)
        {
            AgentPortText.Text = "5000";
        }

        if (AgentLogsText != null)
        {
            AgentLogsText.Text = "Monitor health check: OK" + Environment.NewLine +
                                 "Diagnostics available in Settings > Monitor.";
        }
    }

    private static string BuildTabSlug(string? header, int index)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return $"tab{index + 1}";
        }

        var builder = new StringBuilder();
        foreach (var character in header.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if ((character == ' ' || character == '-' || character == '_') &&
                     builder.Length > 0 &&
                     builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var normalized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? $"tab{index + 1}" : normalized;
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        App.PrivacyChanged -= OnPrivacyChanged;
    }

    private void OnPrivacyChanged(object? sender, bool isPrivacyMode)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnPrivacyChanged(sender, isPrivacyMode));
            return;
        }

        _isPrivacyMode = isPrivacyMode;
        _preferences.IsPrivacyMode = isPrivacyMode;
        UpdatePrivacyButtonState();
        PopulateProviders();
    }

    private void UpdatePrivacyButtonState()
    {
        if (PrivacyBtn == null)
        {
            return;
        }

        PrivacyBtn.Content = _isPrivacyMode ? "\uE72E" : "\uE785";
        PrivacyBtn.Foreground = _isPrivacyMode
            ? Brushes.Gold
            : (TryFindResource("SecondaryText") as Brush ?? Brushes.Gray);
    }

    private async Task UpdateAgentStatusAsync()
    {
        try
        {
            // Check if agent is running
            var isRunning = await AgentLauncher.IsAgentRunningAsync();
            
            // Get the actual port from the agent
            int port = await AgentLauncher.GetAgentPortAsync();
            
            if (AgentStatusText != null)
            {
                AgentStatusText.Text = isRunning ? "Running" : "Not Running";
            }
            
            // Update port display
            if (FindName("AgentPortText") is TextBlock portText)
            {
                portText.Text = port.ToString();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update agent status: {ex.Message}");
            if (AgentStatusText != null)
            {
                AgentStatusText.Text = "Error";
            }
        }
        finally
        {
            RefreshDiagnosticsLog();
        }
    }

    private void RefreshDiagnosticsLog()
    {
        if (AgentLogsText == null)
        {
            return;
        }

        if (_isDeterministicScreenshotMode)
        {
            AgentLogsText.Text = "Monitor health check: OK" + Environment.NewLine +
                                 "Diagnostics available in Settings > Monitor.";
            AgentLogsText.ScrollToEnd();
            return;
        }

        var logs = AgentService.DiagnosticsLog;
        var lines = new List<string>();
        if (logs.Count == 0)
        {
            lines.Add("No diagnostics captured yet.");
        }
        else
        {
            lines.AddRange(logs);
        }

        var telemetry = AgentService.GetTelemetrySnapshot();
        lines.Add("---- Slim Telemetry ----");
        lines.Add(
            $"Usage: count={telemetry.UsageRequestCount}, avg={telemetry.UsageAverageLatencyMs:F1}ms, last={telemetry.UsageLastLatencyMs}ms, errors={telemetry.UsageErrorCount} ({telemetry.UsageErrorRatePercent:F1}%)");
        lines.Add(
            $"Refresh: count={telemetry.RefreshRequestCount}, avg={telemetry.RefreshAverageLatencyMs:F1}ms, last={telemetry.RefreshLastLatencyMs}ms, errors={telemetry.RefreshErrorCount} ({telemetry.RefreshErrorRatePercent:F1}%)");

        AgentLogsText.Text = string.Join(Environment.NewLine, lines);
        AgentLogsText.ScrollToEnd();
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await _agentService.GetHistoryAsync(100);
            HistoryDataGrid.ItemsSource = history;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load history: {ex.Message}");
        }
    }

    private void PopulateProviders()
    {
        ProvidersStack.Children.Clear();

        if (_configs.Count == 0)
        {
            ProvidersStack.Children.Add(new TextBlock
            {
                Text = "No providers configured. Click 'Scan for Keys' to discover API keys.",
                Foreground = FindResource("TertiaryText") as SolidColorBrush,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11
            });
            return;
        }

        var groupedConfigs = _configs.OrderBy(c => c.ProviderId).ToList();

        foreach (var config in groupedConfigs)
        {
            var usage = _usages.FirstOrDefault(u => u.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));
            AddProviderCard(config, usage);
        }
    }

    private void AddProviderCard(ProviderConfig config, ProviderUsage? usage)
    {
        // Compact card with minimal padding
        var card = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(10, 8, 10, 8)
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorder");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Inputs

        // Header: Icon + Name
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        
        // Small icon (16x16)
        var icon = CreateProviderIcon(config.ProviderId);
        icon.Width = 16;
        icon.Height = 16;
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        headerPanel.Children.Add(icon);

        // Display name
        var displayName = config.ProviderId switch {
            "antigravity" => "Google Antigravity",
            "gemini-cli" => "Google Gemini",
            "github-copilot" => "GitHub Copilot",
            "openai" => "OpenAI (Codex)",
            "minimax" => "Minimax (China)",
            "minimax-io" => "Minimax (International)",
            "opencode" => "OpenCode",
            "claude-code" => "Claude Code",
            "zai-coding-plan" => "Z.ai Coding Plan",
            _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                config.ProviderId.Replace("_", " ").Replace("-", " "))
        };

        var title = new TextBlock
        {
            Text = displayName,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 120
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        headerPanel.Children.Add(title);

        // Tray checkbox
        var trayCheckBox = new CheckBox
        {
            Content = "Tray",
            IsChecked = config.ShowInTray,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(12, 0, 0, 0)
        };
        trayCheckBox.SetResourceReference(CheckBox.ForegroundProperty, "SecondaryText");
        trayCheckBox.Checked += (s, e) =>
        {
            config.ShowInTray = true;
            SettingsChanged = true;
            RefreshTrayIcons();
        };
        trayCheckBox.Unchecked += (s, e) =>
        {
            config.ShowInTray = false;
            SettingsChanged = true;
            RefreshTrayIcons();
        };
        headerPanel.Children.Add(trayCheckBox);

        // Notification checkbox
        var notifyCheckBox = new CheckBox
        {
            Content = "Notify",
            IsChecked = config.EnableNotifications,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(8, 0, 0, 0)
        };
        notifyCheckBox.SetResourceReference(CheckBox.ForegroundProperty, "SecondaryText");
        notifyCheckBox.Checked += (s, e) =>
        {
            config.EnableNotifications = true;
            SettingsChanged = true;
        };
        notifyCheckBox.Unchecked += (s, e) =>
        {
            config.EnableNotifications = false;
            SettingsChanged = true;
        };
        headerPanel.Children.Add(notifyCheckBox);

        // Status badge if not configured
        bool isInactive = string.IsNullOrEmpty(config.ApiKey);
        if (config.ProviderId == "antigravity")
        {
            isInactive = usage == null || !usage.IsAvailable;
        }

        if (isInactive)
        {
            var status = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(205, 92, 92)), // IndianRed - pastel red
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(8, 3, 8, 3)
            };

            var badgeText = new TextBlock 
            { 
                Text = "Inactive", 
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 240)), // Muted white
                FontWeight = FontWeights.SemiBold
            };
            status.Child = badgeText;
            headerPanel.Children.Add(status);
        }

        grid.Children.Add(headerPanel);

        // Input row
        var keyPanel = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        keyPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (config.ProviderId == "antigravity")
        {
            // Antigravity: Auto-Detection
            var statusPanel = new StackPanel { Orientation = Orientation.Vertical };
            bool isConnected = usage != null && usage.IsAvailable;
            string accountInfo = usage?.AccountName ?? "Unknown";
            var displayAccount = _isPrivacyMode
                ? MaskAccountIdentifier(accountInfo)
                : accountInfo;

            var statusText = new TextBlock
            {
                Text = isConnected ? $"Auto-Detected ({displayAccount})" : "Searching for local process...",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                FontStyle = isConnected ? FontStyles.Normal : FontStyles.Italic
            };
            statusText.SetResourceReference(TextBlock.ForegroundProperty, 
                isConnected ? "ProgressBarGreen" : "TertiaryText");

            statusPanel.Children.Add(statusText);

            var antigravitySubmodels = usage?.Details?
                .Select(d => d.Name)
                .Where(name =>
                    !string.IsNullOrWhiteSpace(name) &&
                    !name.StartsWith("[", StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (antigravitySubmodels is { Count: > 0 })
            {
                var modelsText = new TextBlock
                {
                    Text = $"Models: {string.Join(", ", antigravitySubmodels)}",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 10,
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                modelsText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
                statusPanel.Children.Add(modelsText);
            }

            Grid.SetColumn(statusPanel, 0);
            keyPanel.Children.Add(statusPanel);
        }
        else if (config.ProviderId == "github-copilot")
        {
            // GitHub Copilot: Show username (if available) - privacy mode only shows masked username
            var statusPanel = new StackPanel { Orientation = Orientation.Horizontal };
            string? username = usage?.AccountName;
            bool hasUsername = !string.IsNullOrEmpty(username) && username != "Unknown";

            bool isAuthenticated = !string.IsNullOrEmpty(config.ApiKey);

            string displayText;
            if (!isAuthenticated)
            {
                displayText = "Not Authenticated";
            }
            else if (!hasUsername)
            {
                displayText = "Authenticated (click Refresh to load username)";
            }
            else if (_isPrivacyMode && username != null)
            {
                displayText = $"Authenticated ({MaskAccountIdentifier(username)})";
            }
            else
            {
                // Normal mode: show full text with username
                displayText = $"Authenticated ({username})";
            }

            var statusText = new TextBlock
            {
                Text = displayText,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11
            };
            statusText.SetResourceReference(TextBlock.ForegroundProperty, 
                isAuthenticated ? "ProgressBarGreen" : "TertiaryText");

            statusPanel.Children.Add(statusText);
            Grid.SetColumn(statusPanel, 0);
            keyPanel.Children.Add(statusPanel);
        }
        else
        {
            // Standard API Key Input
            var displayKey = config.ApiKey;
            if (_isPrivacyMode && !string.IsNullOrEmpty(displayKey))
            {
                if (displayKey.Length > 8)
                    displayKey = displayKey.Substring(0, 4) + "****" + displayKey.Substring(displayKey.Length - 4);
                else
                    displayKey = "****";
            }

            var keyBox = new TextBox
            {
                Text = displayKey,
                Tag = config,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 11,
                IsReadOnly = _isPrivacyMode
            };
            
            if (!_isPrivacyMode)
            {
                keyBox.TextChanged += (s, e) => {
                    config.ApiKey = keyBox.Text;
                    SettingsChanged = true;
                };
            }

            Grid.SetColumn(keyBox, 0);
            keyPanel.Children.Add(keyBox);
        }

        Grid.SetRow(keyPanel, 1);
        grid.Children.Add(keyPanel);

        var subTrayDetails = usage?.Details?
            .Where(d =>
                !string.IsNullOrWhiteSpace(d.Name) &&
                !d.Name.StartsWith("[", StringComparison.Ordinal))
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (subTrayDetails is { Count: > 0 })
        {
            config.EnabledSubTrays ??= new List<string>();

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var separator = new Border
            {
                Height = 1,
                Margin = new Thickness(0, 8, 0, 8)
            };
            separator.SetResourceReference(Border.BackgroundProperty, "Separator");
            Grid.SetRow(separator, 2);
            grid.Children.Add(separator);

            var subTrayPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };

            var subTrayTitle = new TextBlock
            {
                Text = "Sub-tray icons",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            subTrayTitle.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            subTrayPanel.Children.Add(subTrayTitle);

            foreach (var detail in subTrayDetails)
            {
                var subTrayCheckbox = new CheckBox
                {
                    Content = detail.Name,
                    IsChecked = config.EnabledSubTrays.Contains(detail.Name, StringComparer.OrdinalIgnoreCase),
                    FontSize = 10,
                    Margin = new Thickness(0, 1, 0, 1),
                    Cursor = Cursors.Hand
                };
                subTrayCheckbox.SetResourceReference(CheckBox.ForegroundProperty, "SecondaryText");
                subTrayCheckbox.Checked += (s, e) =>
                {
                    if (!config.EnabledSubTrays.Contains(detail.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        config.EnabledSubTrays.Add(detail.Name);
                    }

                    SettingsChanged = true;
                    RefreshTrayIcons();
                };
                subTrayCheckbox.Unchecked += (s, e) =>
                {
                    config.EnabledSubTrays.RemoveAll(name => name.Equals(detail.Name, StringComparison.OrdinalIgnoreCase));
                    SettingsChanged = true;
                    RefreshTrayIcons();
                };
                subTrayPanel.Children.Add(subTrayCheckbox);
            }

            Grid.SetRow(subTrayPanel, 3);
            grid.Children.Add(subTrayPanel);
        }

        card.Child = grid;
        ProvidersStack.Children.Add(card);
    }

    private void RefreshTrayIcons()
    {
        if (Application.Current is App app)
        {
            app.UpdateProviderTrayIcons(_usages, _configs, _preferences);
        }
    }

    private async Task<bool> SaveUiPreferencesAsync(bool showErrorDialog = false)
    {
        App.Preferences = _preferences;
        var saved = await UiPreferencesStore.SaveAsync(_preferences);
        if (!saved)
        {
            Debug.WriteLine("Failed to save Slim UI preferences.");
            if (showErrorDialog)
            {
                MessageBox.Show(
                    "Failed to save Slim UI preferences.",
                    "Save Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        return saved;
    }

    private FrameworkElement CreateProviderIcon(string providerId)
    {
        // Map to SVG or create fallback
        var image = new Image();
        image.Source = GetProviderImageSource(providerId);
        return image;
    }

    private ImageSource GetProviderImageSource(string providerId)
    {
        try
        {
            string filename = providerId.ToLower() switch
            {
                "github-copilot" => "github",
                "gemini-cli" => "google",
                "antigravity" => "google",
                "claude-code" => "claude",
                "zai" => "zai",
                "zai-coding-plan" => "zai",
                "minimax" => "minimax",
                "minimax-io" => "minimax",
                "minimax-global" => "minimax",
                "kimi" => "kimi",
                "xiaomi" => "xiaomi",
                _ => providerId.ToLower()
            };

            var appDir = AppDomain.CurrentDomain.BaseDirectory;

            // Try SVG first
            var svgPath = System.IO.Path.Combine(appDir, "Assets", "ProviderLogos", $"{filename}.svg");
            if (System.IO.File.Exists(svgPath))
            {
                // Return a simple colored circle as fallback (SVG loading requires SharpVectors)
                return CreateFallbackIcon(providerId);
            }

            // Try ICO
            var icoPath = System.IO.Path.Combine(appDir, "Assets", "ProviderLogos", $"{filename}.ico");
            if (System.IO.File.Exists(icoPath))
            {
                var icoImage = new System.Windows.Media.Imaging.BitmapImage();
                icoImage.BeginInit();
                icoImage.UriSource = new Uri(icoPath);
                icoImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                icoImage.EndInit();
                icoImage.Freeze();
                return icoImage;
            }
        }
        catch { }

        return CreateFallbackIcon(providerId);
    }

    private ImageSource CreateFallbackIcon(string providerId)
    {
        // Create a simple colored circle as fallback
        var (color, _) = providerId.ToLower() switch
        {
            "openai" => (Brushes.DarkCyan, "AI"),
            "anthropic" => (Brushes.IndianRed, "An"),
            "github-copilot" => (Brushes.MediumPurple, "GH"),
            "gemini" or "google" => (Brushes.DodgerBlue, "G"),
            "deepseek" => (Brushes.DeepSkyBlue, "DS"),
            _ => (Brushes.Gray, "?")
        };

        // Return a drawing image with just a colored rectangle (simplified)
        var drawing = new GeometryDrawing(
            color,
            new Pen(Brushes.Transparent, 0),
            new RectangleGeometry(new Rect(0, 0, 16, 16)));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private string MaskString(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        if (input.Length <= 2)
        {
            return new string('*', input.Length);
        }

        return input[0] + new string('*', input.Length - 2) + input[^1];
    }

    private string MaskAccountIdentifier(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        var atIndex = input.IndexOf('@');
        if (atIndex > 0 && atIndex < input.Length - 1)
        {
            var localPart = input[..atIndex];
            var domainPart = input[(atIndex + 1)..];
            var maskedDomainChars = domainPart.ToCharArray();
            for (var i = 0; i < maskedDomainChars.Length; i++)
            {
                if (maskedDomainChars[i] != '.')
                {
                    maskedDomainChars[i] = '*';
                }
            }

            var maskedDomain = new string(maskedDomainChars);
            if (localPart.Length <= 2)
            {
                return $"{new string('*', localPart.Length)}@{maskedDomain}";
            }

            return $"{localPart[0]}{new string('*', localPart.Length - 2)}{localPart[^1]}@{maskedDomain}";
        }

        return MaskString(input);
    }

    private void PopulateLayoutSettings()
    {
        AlwaysOnTopCheck.IsChecked = _preferences.AlwaysOnTop;
        InvertProgressCheck.IsChecked = _preferences.InvertProgressBar;
        InvertCalculationsCheck.IsChecked = _preferences.InvertCalculations;
        YellowThreshold.Text = _preferences.ColorThresholdYellow.ToString();
        RedThreshold.Text = _preferences.ColorThresholdRed.ToString();
        
        // Font settings
        PopulateFontComboBox();
        FontFamilyCombo.SelectedItem = _preferences.FontFamily;
        FontSizeBox.Text = _preferences.FontSize.ToString();
        FontBoldCheck.IsChecked = _preferences.FontBold;
        FontItalicCheck.IsChecked = _preferences.FontItalic;
        UpdateFontPreview();
    }

    private void PopulateFontComboBox()
    {
        // Get all system fonts
        var fonts = System.Windows.Media.Fonts.GetFontFamilies(new Uri("pack://application:,,,/"))
            .Select(ff => ff.FamilyNames.FirstOrDefault().Value ?? ff.Source)
            .OrderBy(f => f)
            .ToList();
        
        // If no fonts from pack URI, try alternative method
        if (fonts.Count == 0)
        {
            fonts = System.Windows.Media.Fonts.GetFontFamilies(Environment.GetFolderPath(Environment.SpecialFolder.Fonts))
                .Select(ff => ff.FamilyNames.FirstOrDefault().Value ?? ff.Source)
                .OrderBy(f => f)
                .ToList();
        }
        
        // Fallback to common fonts if still empty
        if (fonts.Count == 0)
        {
            fonts = new List<string>
            {
                "Arial", "Calibri", "Cambria", "Comic Sans MS", "Consolas", "Courier New",
                "Georgia", "Helvetica", "Lucida Console", "Segoe UI", "Tahoma", "Times New Roman",
                "Trebuchet MS", "Verdana"
            }.OrderBy(f => f).ToList();
        }
        
        FontFamilyCombo.ItemsSource = fonts;
    }

    private void UpdateFontPreview()
    {
        if (FontPreviewText == null) return;
        
        // Update font family
        if (!string.IsNullOrEmpty(_preferences.FontFamily))
        {
            FontPreviewText.FontFamily = new System.Windows.Media.FontFamily(_preferences.FontFamily);
        }
        
        // Update font size
        FontPreviewText.FontSize = _preferences.FontSize > 0 ? _preferences.FontSize : 12;
        
        // Update font weight
        FontPreviewText.FontWeight = _preferences.FontBold ? FontWeights.Bold : FontWeights.Normal;
        
        // Update font style
        FontPreviewText.FontStyle = _preferences.FontItalic ? FontStyles.Italic : FontStyles.Normal;
    }

    private void ResetFontBtn_Click(object sender, RoutedEventArgs e)
    {
        // Reset to defaults
        _preferences.FontFamily = "Segoe UI";
        _preferences.FontSize = 12;
        _preferences.FontBold = false;
        _preferences.FontItalic = false;
        
        // Update UI
        FontFamilyCombo.SelectedItem = _preferences.FontFamily;
        FontSizeBox.Text = _preferences.FontSize.ToString();
        FontBoldCheck.IsChecked = _preferences.FontBold;
        FontItalicCheck.IsChecked = _preferences.FontItalic;
        UpdateFontPreview();
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontFamilyCombo.SelectedItem is string font)
        {
            _preferences.FontFamily = font;
            UpdateFontPreview();
        }
    }

    private void FontSizeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(FontSizeBox.Text, out int size) && size > 0 && size <= 72)
        {
            _preferences.FontSize = size;
            UpdateFontPreview();
        }
    }

    private void FontBoldCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        _preferences.FontBold = FontBoldCheck.IsChecked ?? false;
        UpdateFontPreview();
    }

    private void FontItalicCheck_CheckedChanged(object sender, RoutedEventArgs e)
    {
        _preferences.FontItalic = FontItalicCheck.IsChecked ?? false;
        UpdateFontPreview();
    }

    private async void PrivacyBtn_Click(object sender, RoutedEventArgs e)
    {
        var newPrivacyMode = !_isPrivacyMode;
        _preferences.IsPrivacyMode = newPrivacyMode;
        App.SetPrivacyMode(newPrivacyMode);
        await SaveUiPreferencesAsync();
        SettingsChanged = true;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void ScanBtn_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("API key scanning is not yet implemented in AI Usage Tracker.", 
            "Not Implemented", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Trigger refresh on agent
            await _agentService.TriggerRefreshAsync();
            
            // Wait a moment for refresh to complete
            await Task.Delay(2000);
            
            // Reload data
            await LoadDataAsync();
            
            MessageBox.Show("Data refreshed successfully.", "Refresh Complete", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to refresh data: {ex.Message}", "Refresh Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RefreshHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var history = await _agentService.GetHistoryAsync(100);
            HistoryDataGrid.ItemsSource = history;
            
            if (history.Count == 0)
            {
                MessageBox.Show("No history data available. The agent may not have collected any data yet.", 
                    "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load history: {ex.Message}", "History Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        HistoryDataGrid.ItemsSource = null;
    }

    private async void RestartAgentBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Kill any running agent process
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("AIUsageTracker.Monitor")
                .Concat(System.Diagnostics.Process.GetProcessesByName("AIUsageTracker.Monitor")))
            {
                try { process.Kill(); } catch { }
            }
            
            await Task.Delay(1000);
            
            // Restart agent
            if (await AgentLauncher.StartAgentAsync())
            {
                MessageBox.Show("Monitor restarted successfully.", "Restart Complete", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to restart Monitor.", "Restart Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to restart Monitor: {ex.Message}", "Restart Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshDiagnosticsLog();
        }
    }

    private async void CheckHealthBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (isRunning, port) = await AgentLauncher.IsAgentRunningWithPortAsync();
            var status = isRunning ? "Running" : "Not Running";
            
            MessageBox.Show($"Monitor Status: {status}\n\nPort: {port}", "Health Check", 
                MessageBoxButton.OK, isRunning ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to check health: {ex.Message}", "Health Check Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshDiagnosticsLog();
        }
    }

    private async void ExportDiagnosticsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _agentService.RefreshPortAsync();
            await _agentService.RefreshAgentInfoAsync();

            var (isRunning, port) = await AgentLauncher.IsAgentRunningWithPortAsync();
            var healthDetails = await _agentService.GetHealthDetailsAsync();
            var diagnosticsDetails = await _agentService.GetDiagnosticsDetailsAsync();

            var saveDialog = new SaveFileDialog
            {
                FileName = $"ai-usage-tracker-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = ".txt",
                AddExtension = true
            };

            if (saveDialog.ShowDialog(this) != true)
            {
                return;
            }

            var telemetry = AgentService.GetTelemetrySnapshot();
            var bundle = new StringBuilder();
            bundle.AppendLine("AI Usage Tracker - Diagnostics Bundle");
            bundle.AppendLine($"GeneratedAtUtc: {DateTime.UtcNow:O}");
            bundle.AppendLine($"SlimVersion: {typeof(SettingsWindow).Assembly.GetName().Version?.ToString() ?? "unknown"}");
            bundle.AppendLine($"AgentUrl: {_agentService.AgentUrl}");
            bundle.AppendLine($"AgentRunning: {isRunning}");
            bundle.AppendLine($"AgentPort: {port}");
            bundle.AppendLine();

            bundle.AppendLine("=== Monitor Health ===");
            bundle.AppendLine(FormatJsonForBundle(healthDetails));
            bundle.AppendLine();

            bundle.AppendLine("=== Monitor Diagnostics ===");
            bundle.AppendLine(FormatJsonForBundle(diagnosticsDetails));
            bundle.AppendLine();

            bundle.AppendLine("=== Monitor Errors (agent.json) ===");
            if (_agentService.LastAgentErrors.Count == 0)
            {
                bundle.AppendLine("None");
            }
            else
            {
                foreach (var error in _agentService.LastAgentErrors)
                {
                    bundle.AppendLine($"- {error}");
                }
            }
            bundle.AppendLine();

            bundle.AppendLine("=== Slim Telemetry ===");
            bundle.AppendLine(
                $"Usage: count={telemetry.UsageRequestCount}, avg={telemetry.UsageAverageLatencyMs:F1}ms, last={telemetry.UsageLastLatencyMs}ms, errors={telemetry.UsageErrorCount} ({telemetry.UsageErrorRatePercent:F1}%)");
            bundle.AppendLine(
                $"Refresh: count={telemetry.RefreshRequestCount}, avg={telemetry.RefreshAverageLatencyMs:F1}ms, last={telemetry.RefreshLastLatencyMs}ms, errors={telemetry.RefreshErrorCount} ({telemetry.RefreshErrorRatePercent:F1}%)");
            bundle.AppendLine();

            bundle.AppendLine("=== Slim Diagnostics Log ===");
            var diagnosticsLog = AgentService.DiagnosticsLog;
            if (diagnosticsLog.Count == 0)
            {
                bundle.AppendLine("No diagnostics captured yet.");
            }
            else
            {
                foreach (var line in diagnosticsLog)
                {
                    bundle.AppendLine(line);
                }
            }

            await File.WriteAllTextAsync(saveDialog.FileName, bundle.ToString());
            MessageBox.Show($"Diagnostics bundle saved to:\n{saveDialog.FileName}", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to export diagnostics bundle: {ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshDiagnosticsLog();
        }
    }

    private static string FormatJsonForBundle(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "(empty)";
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch
        {
            return content;
        }
    }

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        _preferences.AlwaysOnTop = AlwaysOnTopCheck.IsChecked ?? true;
        _preferences.InvertProgressBar = InvertProgressCheck.IsChecked ?? false;
        _preferences.InvertCalculations = InvertCalculationsCheck.IsChecked ?? false;
        
        if (int.TryParse(YellowThreshold.Text, out int yellow))
            _preferences.ColorThresholdYellow = yellow;
        if (int.TryParse(RedThreshold.Text, out int red))
            _preferences.ColorThresholdRed = red;
        
        // Ensure font settings are saved
        if (FontFamilyCombo.SelectedItem is string font)
            _preferences.FontFamily = font;
        if (int.TryParse(FontSizeBox.Text, out int size) && size > 0 && size <= 72)
            _preferences.FontSize = size;
        _preferences.FontBold = FontBoldCheck.IsChecked ?? false;
        _preferences.FontItalic = FontItalicCheck.IsChecked ?? false;
        _preferences.IsPrivacyMode = _isPrivacyMode;

        var prefsSaved = await SaveUiPreferencesAsync(showErrorDialog: true);
        if (!prefsSaved)
        {
            return;
        }

        var failedConfigs = new List<string>();
        foreach (var config in _configs)
        {
            var saved = await _agentService.SaveConfigAsync(config);
            if (!saved)
            {
                failedConfigs.Add(config.ProviderId);
            }
        }

        if (failedConfigs.Count > 0)
        {
            MessageBox.Show(
                $"Failed to save provider settings for: {string.Join(", ", failedConfigs)}",
                "Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        RefreshTrayIcons();
        
        SettingsChanged = true;
        this.Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }
}


