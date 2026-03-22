// <copyright file="CardDesignerWindow.xaml.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.UI.Slim.ViewModels;

namespace AIUsageTracker.UI.Slim;

public partial class CardDesignerWindow : Window
{
    private readonly AppPreferences _preferences;
    private readonly List<ProviderUsage> _sampleUsages;

    public CardDesignerWindow(AppPreferences preferences, IReadOnlyList<ProviderUsage> usages)
    {
        this.InitializeComponent();
        this._preferences = preferences;
        this._sampleUsages = usages.Take(5).ToList();

        this.PopulateSlotOptions();
        this.ApplyPreset(CardPreset.Detailed);
        this.RenderPreview();
    }

    private enum CardSlotContent
    {
        None,
        PaceBadge,
        ProjectedPercent,
        DailyBudget,
        UsageRate,
        UsedPercent,
        RemainingPercent,
        ResetAbsolute,
        ResetAbsoluteDate,
        ResetRelative,
        AccountName,
        StatusText,
        AuthSource,
    }

    private enum CardPreset
    {
        Compact,
        Detailed,
        PaceFocus,
    }

    private sealed class SlotOption
    {
        public CardSlotContent Value { get; init; }

        public string Label { get; init; } = string.Empty;

        public override string ToString() => this.Label;
    }

    private sealed class UserPreset
    {
        public string Name { get; set; } = string.Empty;

        public CardSlotContent PrimaryBadge { get; set; }

        public CardSlotContent SecondaryBadge { get; set; }

        public CardSlotContent StatusLine { get; set; }

        public CardSlotContent ResetInfo { get; set; }

        public override string ToString() => this.Name;
    }

    private void PopulateSlotOptions()
    {
        var options = new SlotOption[]
        {
            new() { Value = CardSlotContent.None, Label = "(empty)" },
            new() { Value = CardSlotContent.PaceBadge, Label = "Pace badge (On pace / Over pace)" },
            new() { Value = CardSlotContent.ProjectedPercent, Label = "Projected % at reset" },
            new() { Value = CardSlotContent.DailyBudget, Label = "Daily budget (14%/day)" },
            new() { Value = CardSlotContent.UsageRate, Label = "Usage rate (req/hr)" },
            new() { Value = CardSlotContent.UsedPercent, Label = "Used %" },
            new() { Value = CardSlotContent.RemainingPercent, Label = "Remaining %" },
            new() { Value = CardSlotContent.ResetAbsolute, Label = "Reset time (Saturday 17:44)" },
            new() { Value = CardSlotContent.ResetAbsoluteDate, Label = "Reset date (Mar 26, 17:44)" },
            new() { Value = CardSlotContent.ResetRelative, Label = "Reset countdown (4d 22h)" },
            new() { Value = CardSlotContent.AccountName, Label = "Account name" },
            new() { Value = CardSlotContent.StatusText, Label = "Status text" },
            new() { Value = CardSlotContent.AuthSource, Label = "Auth source" },
        };

        foreach (var combo in new[] { this.PrimaryBadgeSlot, this.SecondaryBadgeSlot, this.StatusLineSlot, this.ResetInfoSlot })
        {
            combo.ItemsSource = options;
            combo.DisplayMemberPath = "Label";
            combo.SelectedValuePath = "Value";
        }
    }

    private void ApplyPreset(CardPreset preset)
    {
        switch (preset)
        {
            case CardPreset.Compact:
                this.PrimaryBadgeSlot.SelectedValue = CardSlotContent.UsedPercent;
                this.SecondaryBadgeSlot.SelectedValue = CardSlotContent.None;
                this.StatusLineSlot.SelectedValue = CardSlotContent.None;
                this.ResetInfoSlot.SelectedValue = CardSlotContent.ResetAbsolute;
                break;

            case CardPreset.Detailed:
                this.PrimaryBadgeSlot.SelectedValue = CardSlotContent.PaceBadge;
                this.SecondaryBadgeSlot.SelectedValue = CardSlotContent.UsageRate;
                this.StatusLineSlot.SelectedValue = CardSlotContent.StatusText;
                this.ResetInfoSlot.SelectedValue = CardSlotContent.ResetAbsolute;
                break;

            case CardPreset.PaceFocus:
                this.PrimaryBadgeSlot.SelectedValue = CardSlotContent.PaceBadge;
                this.SecondaryBadgeSlot.SelectedValue = CardSlotContent.ProjectedPercent;
                this.StatusLineSlot.SelectedValue = CardSlotContent.DailyBudget;
                this.ResetInfoSlot.SelectedValue = CardSlotContent.ResetAbsolute;
                break;
        }
    }

    private void RenderPreview()
    {
        this.PreviewStack.Children.Clear();

        if (this._sampleUsages.Count == 0)
        {
            this.PreviewStack.Children.Add(new TextBlock
            {
                Text = "No provider data available. Start the Monitor first.",
                Foreground = (Brush)this.FindResource("SecondaryText"),
                Margin = new Thickness(10),
            });
            return;
        }

        foreach (var usage in this._sampleUsages)
        {
            var card = this.BuildPreviewCard(usage);
            this.PreviewStack.Children.Add(card);
        }
    }

    private Border BuildPreviewCard(ProviderUsage usage)
    {
        var isCompact = this.CompactModeCheck?.IsChecked ?? false;
        var displayName = ProviderMetadataCatalog.ResolveDisplayLabel(usage);
        var primaryText = this.ResolveSlotText(usage, (CardSlotContent?)this.PrimaryBadgeSlot.SelectedValue ?? CardSlotContent.None);
        var secondaryText = this.ResolveSlotText(usage, (CardSlotContent?)this.SecondaryBadgeSlot.SelectedValue ?? CardSlotContent.None);
        var statusText = this.ResolveSlotText(usage, (CardSlotContent?)this.StatusLineSlot.SelectedValue ?? CardSlotContent.None);
        var resetText = this.ResolveSlotText(usage, (CardSlotContent?)this.ResetInfoSlot.SelectedValue ?? CardSlotContent.None);

        var usedPercent = usage.UsedPercent;
        var barWidth = Math.Max(0, Math.Min(100, usedPercent));

        var useBackgroundBar = this.BackgroundBarCheck?.IsChecked ?? false;

        return isCompact
            ? this.BuildCompactCard(displayName, primaryText, secondaryText, statusText, resetText, usedPercent, barWidth, usage, useBackgroundBar)
            : this.BuildDetailedCard(displayName, primaryText, secondaryText, statusText, resetText, usedPercent, barWidth, usage, useBackgroundBar);
    }

    private Border BuildCompactCard(
        string displayName, string primaryText, string secondaryText,
        string statusText, string resetText, double usedPercent, double barWidth,
        ProviderUsage usage, bool useBackgroundBar)
    {
        var card = new Border
        {
            BorderBrush = (Brush)this.FindResource("CardBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 0, 0, 1),
            ClipToBounds = true,
        };

        // Card background: percentage fill as subtle background color
        var bgGrid = new Grid();

        if (useBackgroundBar && usage.IsAvailable && usage.IsQuotaBased && barWidth > 0)
        {
            bgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(barWidth, GridUnitType.Star) });
            bgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 100 - barWidth), GridUnitType.Star) });

            var fill = new Border
            {
                Background = this.GetBarColor(usedPercent),
                Opacity = 0.15,
            };
            Grid.SetColumn(fill, 0);
            bgGrid.Children.Add(fill);

            var empty = new Border { Background = (Brush)this.FindResource("CardBackground") };
            Grid.SetColumn(empty, 1);
            bgGrid.Children.Add(empty);
        }
        else
        {
            bgGrid.Children.Add(new Border { Background = (Brush)this.FindResource("CardBackground") });
        }

        // Content row on top of the background
        var row = new DockPanel { Margin = new Thickness(6, 3, 6, 3) };

        void AddRight(string text, Brush fg, double fontSize = 8.5)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var block = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                Foreground = fg,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(block, Dock.Right);
            row.Children.Add(block);
        }

        AddRight(resetText, (Brush)this.FindResource("StatusTextWarning"), 8.5);
        AddRight(primaryText, this.GetBadgeColor(primaryText), 8);
        AddRight(secondaryText, (Brush)this.FindResource("TertiaryText"), 8);
        AddRight(statusText, (Brush)this.FindResource("SecondaryText"), 8);

        var nameBlock = new TextBlock
        {
            Text = displayName,
            FontSize = 10,
            Foreground = (Brush)this.FindResource("PrimaryText"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        row.Children.Add(nameBlock);

        // Layer: background grid + content row
        bgGrid.Children.Add(row);
        card.Child = bgGrid;
        return card;
    }

    private Border BuildDetailedCard(
        string displayName, string primaryText, string secondaryText,
        string statusText, string resetText, double usedPercent, double barWidth,
        ProviderUsage usage, bool useBackgroundBar)
    {
        var card = new Border
        {
            BorderBrush = (Brush)this.FindResource("CardBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 4),
            ClipToBounds = true,
        };

        // Background: either solid or percentage fill
        var bgGrid = new Grid();
        if (useBackgroundBar && usage.IsAvailable && usage.IsQuotaBased && barWidth > 0)
        {
            bgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(barWidth, GridUnitType.Star) });
            bgGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 100 - barWidth), GridUnitType.Star) });

            var fill = new Border { Background = this.GetBarColor(usedPercent), Opacity = 0.15 };
            Grid.SetColumn(fill, 0);
            bgGrid.Children.Add(fill);

            var empty = new Border { Background = (Brush)this.FindResource("CardBackground") };
            Grid.SetColumn(empty, 1);
            bgGrid.Children.Add(empty);
        }
        else
        {
            bgGrid.Children.Add(new Border { Background = (Brush)this.FindResource("CardBackground") });
        }

        var stack = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };

        // Row 1: Name + badges
        var topRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

        void AddRightBadge(string text, Brush fg, double fontSize, FontWeight weight)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var block = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = weight,
                Foreground = fg,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(block, Dock.Right);
            topRow.Children.Add(block);
        }

        AddRightBadge(resetText, (Brush)this.FindResource("StatusTextWarning"), 10, FontWeights.SemiBold);
        AddRightBadge(primaryText, this.GetBadgeColor(primaryText), 9, FontWeights.Normal);
        AddRightBadge(secondaryText, (Brush)this.FindResource("TertiaryText"), 9, FontWeights.Normal);

        var nameBlock = new TextBlock
        {
            Text = displayName,
            FontSize = 11,
            Foreground = (Brush)this.FindResource("PrimaryText"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        topRow.Children.Add(nameBlock);
        stack.Children.Add(topRow);

        // Row 2: Progress bar (only when not using background bar)
        if (!useBackgroundBar && usage.IsAvailable && usage.IsQuotaBased)
        {
            var barGrid = new Grid { Height = 4, Margin = new Thickness(0, 2, 0, 2) };
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(barWidth, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 100 - barWidth), GridUnitType.Star) });

            var barFill = new Border
            {
                Background = this.GetBarColor(usedPercent),
                Opacity = 0.45,
            };
            Grid.SetColumn(barFill, 0);
            barGrid.Children.Add(barFill);
            stack.Children.Add(barGrid);
        }

        // Row 3: Status line
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            var statusBlock = new TextBlock
            {
                Text = statusText,
                FontSize = 10,
                Foreground = (Brush)this.FindResource("SecondaryText"),
                Margin = new Thickness(0, 2, 0, 0),
            };
            stack.Children.Add(statusBlock);
        }

        bgGrid.Children.Add(stack);
        card.Child = bgGrid;
        return card;
    }

    private string ResolveSlotText(ProviderUsage usage, CardSlotContent slot)
    {
        return slot switch
        {
            CardSlotContent.None => string.Empty,
            CardSlotContent.PaceBadge => GetPaceBadgeText(usage),
            CardSlotContent.ProjectedPercent => GetProjectedText(usage),
            CardSlotContent.DailyBudget => GetDailyBudgetText(usage),
            CardSlotContent.UsageRate => usage.UsagePerHour.HasValue ? $"{usage.UsagePerHour.Value:F1}/hr" : string.Empty,
            CardSlotContent.UsedPercent => $"{usage.UsedPercent:F0}% used",
            CardSlotContent.RemainingPercent => $"{Math.Max(0, 100 - usage.UsedPercent):F0}% remaining",
            CardSlotContent.ResetAbsolute => GetAbsoluteResetText(usage),
            CardSlotContent.ResetAbsoluteDate => GetAbsoluteDateResetText(usage),
            CardSlotContent.ResetRelative => GetRelativeResetText(usage),
            CardSlotContent.AccountName => usage.AccountName ?? string.Empty,
            CardSlotContent.StatusText => usage.Description ?? string.Empty,
            CardSlotContent.AuthSource => usage.AuthSource ?? string.Empty,
            _ => string.Empty,
        };
    }

    private static string GetPaceBadgeText(ProviderUsage usage)
    {
        if (!usage.PeriodDuration.HasValue || !usage.NextResetTime.HasValue)
        {
            return string.Empty;
        }

        if (usage.NextResetTime.Value.Ticks < usage.PeriodDuration.Value.Ticks)
        {
            return string.Empty;
        }

        var projected = UsageMath.CalculateProjectedFinalPercent(
            usage.UsedPercent,
            usage.NextResetTime.Value.ToUniversalTime(),
            usage.PeriodDuration.Value);

        if (projected >= 100.0)
        {
            return "Over pace";
        }

        return projected < 90.0 ? "On pace" : string.Empty;
    }

    private static string GetProjectedText(ProviderUsage usage)
    {
        if (!usage.PeriodDuration.HasValue || !usage.NextResetTime.HasValue)
        {
            return string.Empty;
        }

        if (usage.NextResetTime.Value.Ticks < usage.PeriodDuration.Value.Ticks)
        {
            return string.Empty;
        }

        var projected = UsageMath.CalculateProjectedFinalPercent(
            usage.UsedPercent,
            usage.NextResetTime.Value.ToUniversalTime(),
            usage.PeriodDuration.Value);

        return $"Projected: {projected:F0}%";
    }

    private static string GetDailyBudgetText(ProviderUsage usage)
    {
        if (!usage.PeriodDuration.HasValue || usage.PeriodDuration.Value.TotalDays < 1)
        {
            return string.Empty;
        }

        var dailyBudget = 100.0 / usage.PeriodDuration.Value.TotalDays;
        return $"{dailyBudget:F0}%/day budget";
    }

    private static string GetAbsoluteResetText(ProviderUsage usage)
    {
        if (!usage.NextResetTime.HasValue)
        {
            return string.Empty;
        }

        var local = usage.NextResetTime.Value.Kind == DateTimeKind.Utc
            ? usage.NextResetTime.Value.ToLocalTime()
            : usage.NextResetTime.Value;
        var diff = local - DateTime.Now;

        if (diff.TotalSeconds <= 0)
        {
            return "now";
        }

        if (local.Date == DateTime.Today)
        {
            return local.ToString("HH:mm");
        }

        if (local.Date == DateTime.Today.AddDays(1))
        {
            return $"Tomorrow {local:HH:mm}";
        }

        if (diff.TotalDays < 7)
        {
            return $"{local:dddd HH:mm}";
        }

        return $"{local:MMM d HH:mm}";
    }

    private static string GetAbsoluteDateResetText(ProviderUsage usage)
    {
        if (!usage.NextResetTime.HasValue)
        {
            return string.Empty;
        }

        var local = usage.NextResetTime.Value.Kind == DateTimeKind.Utc
            ? usage.NextResetTime.Value.ToLocalTime()
            : usage.NextResetTime.Value;

        return $"{local:MMM d, HH:mm}";
    }

    private static string GetRelativeResetText(ProviderUsage usage)
    {
        if (!usage.NextResetTime.HasValue)
        {
            return string.Empty;
        }

        var diff = usage.NextResetTime.Value - DateTime.Now;
        if (diff.TotalSeconds <= 0)
        {
            return "0m";
        }

        if (diff.TotalDays >= 1)
        {
            return $"{diff.Days}d {diff.Hours}h";
        }

        return diff.TotalHours >= 1 ? $"{diff.Hours}h {diff.Minutes}m" : $"{diff.Minutes}m";
    }

    private Brush GetBadgeColor(string text)
    {
        if (text.Contains("Over pace", StringComparison.OrdinalIgnoreCase))
        {
            return Brushes.OrangeRed;
        }

        if (text.Contains("On pace", StringComparison.OrdinalIgnoreCase))
        {
            return Brushes.MediumSeaGreen;
        }

        return (Brush)this.FindResource("SecondaryText");
    }

    private Brush GetBarColor(double usedPercent)
    {
        if (usedPercent >= this._preferences.ColorThresholdRed)
        {
            return Brushes.OrangeRed;
        }

        if (usedPercent >= this._preferences.ColorThresholdYellow)
        {
            return Brushes.Gold;
        }

        return Brushes.MediumSeaGreen;
    }

    // --- User presets ---

    private readonly List<UserPreset> _userPresets = new();

    private void RefreshUserPresetCombo()
    {
        this.UserPresetCombo.ItemsSource = null;
        this.UserPresetCombo.ItemsSource = this._userPresets;
    }

    private CardSlotContent GetSlotValue(ComboBox combo)
    {
        return (combo.SelectedValue as CardSlotContent?) ?? CardSlotContent.None;
    }

    private void SavePresetBtn_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptForName("Save Preset", "Preset name:");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var existing = this._userPresets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            this._userPresets.Remove(existing);
        }

        this._userPresets.Add(new UserPreset
        {
            Name = name,
            PrimaryBadge = this.GetSlotValue(this.PrimaryBadgeSlot),
            SecondaryBadge = this.GetSlotValue(this.SecondaryBadgeSlot),
            StatusLine = this.GetSlotValue(this.StatusLineSlot),
            ResetInfo = this.GetSlotValue(this.ResetInfoSlot),
        });

        this.RefreshUserPresetCombo();
        this.UserPresetCombo.SelectedItem = this._userPresets.Last();
    }

    private void DeletePresetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (this.UserPresetCombo.SelectedItem is UserPreset preset)
        {
            this._userPresets.Remove(preset);
            this.RefreshUserPresetCombo();
        }
    }

    private void UserPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.UserPresetCombo.SelectedItem is not UserPreset preset)
        {
            return;
        }

        this.PrimaryBadgeSlot.SelectedValue = preset.PrimaryBadge;
        this.SecondaryBadgeSlot.SelectedValue = preset.SecondaryBadge;
        this.StatusLineSlot.SelectedValue = preset.StatusLine;
        this.ResetInfoSlot.SelectedValue = preset.ResetInfo;
    }

    private static string? PromptForName(string title, string prompt)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 320,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
        };

        var textBox = new TextBox { Margin = new Thickness(12, 8, 12, 8), Text = "My Preset" };
        var okButton = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 12, 8), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        okButton.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(12, 12, 12, 0) });
        stack.Children.Add(textBox);
        stack.Children.Add(okButton);
        dialog.Content = stack;

        return dialog.ShowDialog() == true ? textBox.Text : null;
    }

    // --- Event handlers ---

    private void SlotConfig_Changed(object sender, SelectionChangedEventArgs e) => this.RenderPreview();

    private void CompactMode_Changed(object sender, RoutedEventArgs e) => this.RenderPreview();

    private void PresetCompact_Click(object sender, RoutedEventArgs e)
    {
        this.ApplyPreset(CardPreset.Compact);
        this.RenderPreview();
    }

    private void PresetDetailed_Click(object sender, RoutedEventArgs e)
    {
        this.ApplyPreset(CardPreset.Detailed);
        this.RenderPreview();
    }

    private void PresetPace_Click(object sender, RoutedEventArgs e)
    {
        this.ApplyPreset(CardPreset.PaceFocus);
        this.RenderPreview();
    }

    private void ApplyBtn_Click(object sender, RoutedEventArgs e)
    {
        // TODO: save slot configuration to preferences and apply to MainWindow
        MessageBox.Show("Card layout configuration will be saved to preferences in a future update.",
            "Not yet implemented", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => this.DragMove();

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => this.Close();
}
