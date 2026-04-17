// <copyright file="SettingsWindow.Cards.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

public partial class SettingsWindow
{
    private readonly List<UserPreset> _userPresets = new();
    private List<ProviderUsage> _cardPreviewUsages = new();

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

    private void InitializeCardDesigner()
    {
        this._cardPreviewUsages = this._usages
            .OrderByDescending(u => u.IsAvailable && u.UsedPercent > 0 ? 1 : 0)
            .ThenByDescending(u => u.UsedPercent)
            .ThenByDescending(u => u.IsAvailable ? 1 : 0)
            .Take(5)
            .ToList();

        this.PopulateCardSlotOptions();

        // Load saved slot configuration from preferences
        this.PrimaryBadgeSlot.SelectedValue = this._preferences.CardPrimaryBadge;
        this.SecondaryBadgeSlot.SelectedValue = this._preferences.CardSecondaryBadge;
        this.StatusLineSlot.SelectedValue = this._preferences.CardStatusLine;
        this.ResetInfoSlot.SelectedValue = this._preferences.CardResetInfo;
        this.CompactModeCheck.IsChecked = this._preferences.CardCompactMode;
        this.BackgroundBarCheck.IsChecked = this._preferences.CardBackgroundBar;

        this.RenderCardPreview();
    }

    private void PopulateCardSlotOptions()
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

    private void ApplyCardPreset(CardPreset preset)
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

    private void RenderCardPreview()
    {
        if (this.CardPreviewStack == null)
        {
            return;
        }

        this.CardPreviewStack.Children.Clear();

        if (this._cardPreviewUsages.Count == 0)
        {
            this.CardPreviewStack.Children.Add(new TextBlock
            {
                Text = "No provider data available. Start the Monitor first.",
                Foreground = (Brush)this.FindResource("SecondaryText"),
                Margin = new Thickness(10),
            });
            return;
        }

        var showUsed = this._preferences.ShowUsedPercentages;
        var renderer = new ProviderCardRenderer(
            this._preferences,
            this._isPrivacyMode,
            UIHelper.GetResourceBrush,
            _ => new Border { Width = 14, Height = 14 },
            (_, _) => new ToolTip(),
            _ => { },
            UsageMath.FormatRelativeTime);

        foreach (var usage in this._cardPreviewUsages)
        {
            var card = renderer.CreateProviderCard(usage, showUsed);
            this.CardPreviewStack.Children.Add(card);
        }
    }

    private void NotifyMainWindowChanged()
    {
        if (this.Owner is MainWindow mainWindow)
        {
            mainWindow.RenderProviders();
        }
    }

    private void RefreshCardUserPresetCombo()
    {
        this.UserPresetCombo.ItemsSource = null;
        this.UserPresetCombo.ItemsSource = this._userPresets;
    }

    private static CardSlotContent GetSlotValue(ComboBox combo)
    {
        return (combo.SelectedValue as CardSlotContent?) ?? CardSlotContent.None;
    }

    private string? PromptForName(string title, string prompt)
    {
        var res = Application.Current.Resources;
        var dialog = new Window
        {
            Title = title,
            Width = 320,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            Background = res["Background"] as Brush ?? Brushes.Black,
            Foreground = res["PrimaryText"] as Brush ?? Brushes.White,
        };

        var textBox = new TextBox
        {
            Margin = new Thickness(12, 8, 12, 8),
            Text = "My Preset",
            Background = res["ControlBackground"] as Brush ?? Brushes.DarkGray,
            Foreground = res["PrimaryText"] as Brush ?? Brushes.White,
            BorderBrush = res["ControlBorder"] as Brush ?? Brushes.Gray,
            Padding = new Thickness(6, 4, 6, 4),
        };

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 75,
            MinHeight = 23,
            Margin = new Thickness(0, 0, 12, 8),
            Padding = new Thickness(12, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
            Background = res["ButtonBackground"] as Brush ?? Brushes.DarkGray,
            Foreground = res["PrimaryText"] as Brush ?? Brushes.White,
            BorderBrush = res["ControlBorder"] as Brush ?? Brushes.Gray,
        };
        okButton.Click += (_, _) =>
        {
            dialog.DialogResult = true;
            dialog.Close();
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = prompt,
            Margin = new Thickness(12, 12, 12, 0),
            Foreground = res["SecondaryText"] as Brush ?? Brushes.LightGray,
        });
        stack.Children.Add(textBox);
        stack.Children.Add(okButton);
        dialog.Content = stack;

        return dialog.ShowDialog() == true ? textBox.Text : null;
    }

    private void CardSlotConfig_Changed(object sender, SelectionChangedEventArgs e)
    {
        this.SyncCardSlotPreferences();
        this.ScheduleAutoSave();
        this.RenderCardPreview();
        this.NotifyMainWindowChanged();
    }

    private void SyncCardSlotPreferences()
    {
        if (this.PrimaryBadgeSlot.SelectedValue is CardSlotContent primary)
        {
            this._preferences.CardPrimaryBadge = primary;
        }

        if (this.SecondaryBadgeSlot.SelectedValue is CardSlotContent secondary)
        {
            this._preferences.CardSecondaryBadge = secondary;
        }

        if (this.StatusLineSlot.SelectedValue is CardSlotContent status)
        {
            this._preferences.CardStatusLine = status;
        }

        if (this.ResetInfoSlot.SelectedValue is CardSlotContent reset)
        {
            this._preferences.CardResetInfo = reset;
        }
    }

    private void CardCompactMode_Changed(object sender, RoutedEventArgs e)
    {
        this._preferences.CardCompactMode = this.CompactModeCheck?.IsChecked ?? false;
        this._preferences.CardBackgroundBar = this.BackgroundBarCheck?.IsChecked ?? true;
        this.ScheduleAutoSave();
        this.RenderCardPreview();
        this.NotifyMainWindowChanged();
    }

    private void CardPresetCompact_Click(object sender, RoutedEventArgs e)
    {
        this.ApplyCardPreset(CardPreset.Compact);
        this.RenderCardPreview();
    }

    private void CardPresetDetailed_Click(object sender, RoutedEventArgs e)
    {
        this.ApplyCardPreset(CardPreset.Detailed);
        this.RenderCardPreview();
    }

    private void CardPresetPace_Click(object sender, RoutedEventArgs e)
    {
        this.ApplyCardPreset(CardPreset.PaceFocus);
        this.RenderCardPreview();
    }

    private void CardSavePresetBtn_Click(object sender, RoutedEventArgs e)
    {
        var name = this.PromptForName("Save Preset", "Preset name:");
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
            PrimaryBadge = GetSlotValue(this.PrimaryBadgeSlot),
            SecondaryBadge = GetSlotValue(this.SecondaryBadgeSlot),
            StatusLine = GetSlotValue(this.StatusLineSlot),
            ResetInfo = GetSlotValue(this.ResetInfoSlot),
        });

        this.RefreshCardUserPresetCombo();
        this.UserPresetCombo.SelectedItem = this._userPresets[^1];
    }

    private void CardDeletePresetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (this.UserPresetCombo.SelectedItem is UserPreset preset)
        {
            this._userPresets.Remove(preset);
            this.RefreshCardUserPresetCombo();
        }
    }

    private void CardUserPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
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

    /// <summary>
    /// Selects the tab with the given header text.
    /// </summary>
    public void SelectTab(string tabHeader)
    {
        for (var i = 0; i < this.MainTabControl.Items.Count; i++)
        {
            if (this.MainTabControl.Items[i] is TabItem tab &&
                string.Equals(tab.Header?.ToString(), tabHeader, StringComparison.OrdinalIgnoreCase))
            {
                this.MainTabControl.SelectedIndex = i;
                return;
            }
        }
    }
}
