using System;
using System.Diagnostics;
using System.Windows;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.UI.Services
{
    public static class ThemeHelper
    {
        public static void ApplyTheme(AppPreferences prefs)
        {
            // Force Dark Mode for now as requested by user
            var isDark = true; 
            SwitchTheme(isDark);

            // Apply to all open windows
            if (Application.Current != null)
            {
                foreach (Window window in Application.Current.Windows)
                {
                    ApplyThemeToWindow(window, isDark);
                }
            }
        }

        public static void SwitchTheme(bool isDark)
        {
            try
            {
                var appResources = Application.Current.Resources;
                var prefix = isDark ? "Dark" : "Light";

                // Consolidated list of all resource keys to swap
                var resourceKeys = new[]
                {
                    "Background", "HeaderBackground", "FooterBackground", "BorderColor",
                    "ControlBackground", "ControlBorder", "InputBackground",
                    "PrimaryText", "SecondaryText", "TertiaryText", "AccentColor",
                    "ButtonBackground", "ButtonHover", "ButtonPressed", "ButtonForeground",
                    "TabUnselected", "ComboBoxBackground", "ComboBoxItemHover",
                    "CheckBoxForeground", "CardBackground", "CardBorder",
                    "GroupHeaderBackground", "GroupHeaderBorder",
                    "ScrollBarBackground", "ScrollBarForeground", "ScrollBarHover",
                    "LinkForeground", "UpdateBannerBackground", "UpdateButtonBackground",
                    "ProgressBarBackground", "ProgressBarGreen", "ProgressBarYellow", "ProgressBarRed",
                    "StatusTextNormal", "StatusTextMissing", "StatusTextError", "StatusTextWarning", "StatusTextConsole",
                    "PrivacyModeActive", "PrivacyModeInactive", "AccentForeground",
                    "InactiveBadge", "InactiveBadgeText", "AuthStatusBg", "AuthStatusText",
                    "Separator", "CheckboxUnchecked", "PreviewBackground", "PreviewBorder"
                };

                foreach (var key in resourceKeys)
                {
                    var themeKey = $"{prefix}{key}";
                    if (appResources.Contains(themeKey))
                    {
                        appResources[key] = appResources[themeKey];
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WARNING] Failed to switch theme: {ex.Message}");
            }
        }

        public static void ApplyThemeToWindow(Window window, bool isDark)
        {
            if (window == null) return;

            // Most windows use DynamicResource, but we can force update some properties if needed
            // For example, if a window has hardcoded elements that don't use DynamicResource
            
            // In our case, MainWindow and SettingsWindow use DynamicResource on the Window itself
            // Window.Background="{DynamicResource Background}"
            // Window.Foreground="{DynamicResource PrimaryText}"
            
            // If they are already using DynamicResource, updating the Application.Resources key
            // should be enough for any NEWly created elements or bound elements.
        }
    }
}
