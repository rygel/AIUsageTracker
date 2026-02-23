using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.AgentClient;
using System.Runtime.InteropServices;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIUsageTracker.UI.Slim;

public partial class App : Application
{
    public static AgentService AgentService { get; } = new();
    public static AppPreferences Preferences { get; set; } = new();
    public static bool IsPrivacyMode { get; set; } = false;
    private const double ScreenshotScaleFactor = 2.0;
    private const double ScreenshotDpi = 96.0 * ScreenshotScaleFactor;
    private TaskbarIcon? _trayIcon;
    private readonly Dictionary<string, TaskbarIcon> _providerTrayIcons = new();
    private MainWindow? _mainWindow;

    public static event EventHandler<bool>? PrivacyChanged;

    public static void ApplyTheme(AppTheme theme)
    {
        Preferences.Theme = theme;
        var resources = Current?.Resources;
        if (resources == null)
        {
            return;
        }

        switch (theme)
        {
            case AppTheme.Light:
                SetBrushColor(resources, "Background", Color.FromRgb(245, 245, 245));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(229, 229, 229));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(229, 229, 229));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(221, 221, 221));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(26, 26, 26));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(82, 82, 82));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(136, 136, 136));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(37, 99, 235));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(255, 255, 255));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(229, 229, 229));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(238, 238, 238));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(37, 99, 235));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(255, 255, 255));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(221, 221, 221));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(255, 255, 255));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(229, 229, 229));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(255, 255, 255));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(238, 238, 238));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(229, 229, 229));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(202, 138, 4));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(229, 229, 229));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(221, 221, 221));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(255, 255, 255));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(221, 221, 221));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(229, 229, 229));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(170, 170, 170));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(140, 140, 140));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(37, 99, 235));
                break;

            case AppTheme.Corporate:
                SetBrushColor(resources, "Background", Color.FromRgb(15, 23, 42));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(30, 41, 59));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(30, 41, 59));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(51, 65, 85));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(241, 245, 249));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(148, 163, 184));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(100, 116, 139));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(59, 130, 246));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(255, 255, 255));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(51, 65, 85));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(61, 79, 102));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(59, 130, 246));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(30, 41, 59));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(51, 65, 85));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(30, 41, 59));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(51, 65, 85));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(30, 41, 59));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(61, 79, 102));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(51, 65, 85));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(245, 158, 11));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(30, 41, 59));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(51, 65, 85));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(30, 41, 59));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(51, 65, 85));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(30, 41, 59));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(100, 116, 139));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(148, 163, 184));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(96, 165, 250));
                break;

            case AppTheme.Midnight:
                SetBrushColor(resources, "Background", Color.FromRgb(10, 10, 10));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(20, 20, 20));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(20, 20, 20));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(38, 38, 38));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(250, 250, 250));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(136, 136, 136));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(85, 85, 85));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(129, 140, 248));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(255, 255, 255));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(31, 31, 31));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(42, 42, 42));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(129, 140, 248));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(20, 20, 20));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(38, 38, 38));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(20, 20, 20));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(31, 31, 31));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(20, 20, 20));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(42, 42, 42));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(31, 31, 31));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(251, 191, 36));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(20, 20, 20));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(38, 38, 38));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(20, 20, 20));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(38, 38, 38));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(20, 20, 20));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(85, 85, 85));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(136, 136, 136));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(165, 180, 252));
                break;

            case AppTheme.Dracula:
                SetBrushColor(resources, "Background", Color.FromRgb(40, 42, 54));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(52, 55, 70));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(52, 55, 70));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(68, 71, 90));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(248, 248, 242));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(189, 147, 249));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(98, 114, 164));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(255, 121, 198));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(40, 42, 54));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(68, 71, 90));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(80, 84, 108));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(255, 121, 198));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(52, 55, 70));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(68, 71, 90));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(52, 55, 70));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(52, 55, 70));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(52, 55, 70));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(68, 71, 90));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(68, 71, 90));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(255, 184, 108));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(52, 55, 70));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(68, 71, 90));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(52, 55, 70));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(68, 71, 90));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(52, 55, 70));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(98, 114, 164));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(139, 233, 253));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(139, 233, 253));
                break;

            case AppTheme.Nord:
                SetBrushColor(resources, "Background", Color.FromRgb(46, 52, 64));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(76, 86, 106));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(216, 222, 233));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(129, 161, 193));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(136, 192, 208));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(136, 192, 208));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(46, 52, 64));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(67, 76, 94));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(76, 86, 106));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(136, 192, 208));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(76, 86, 106));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(76, 86, 106));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(67, 76, 94));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(235, 203, 139));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(76, 86, 106));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(76, 86, 106));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(129, 161, 193));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(136, 192, 208));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(143, 188, 187));
                break;

            case AppTheme.Monokai:
                SetBrushColor(resources, "Background", Color.FromRgb(39, 40, 34));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(52, 53, 46));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(52, 53, 46));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(73, 72, 62));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(248, 248, 242));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(169, 183, 198));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(117, 113, 94));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(166, 226, 46));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(39, 40, 34));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(73, 72, 62));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(92, 90, 76));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(166, 226, 46));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(52, 53, 46));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(73, 72, 62));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(52, 53, 46));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(52, 53, 46));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(52, 53, 46));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(73, 72, 62));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(73, 72, 62));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(253, 151, 31));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(52, 53, 46));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(73, 72, 62));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(52, 53, 46));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(73, 72, 62));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(52, 53, 46));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(117, 113, 94));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(169, 183, 198));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(102, 217, 239));
                break;

            case AppTheme.OneDark:
                SetBrushColor(resources, "Background", Color.FromRgb(40, 44, 52));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(58, 63, 73));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(171, 178, 191));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(130, 137, 151));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(92, 99, 112));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(97, 175, 239));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(33, 37, 43));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(58, 63, 73));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(73, 80, 92));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(97, 175, 239));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(58, 63, 73));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(58, 63, 73));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(58, 63, 73));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(229, 192, 123));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(58, 63, 73));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(58, 63, 73));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(92, 99, 112));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(130, 137, 151));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(97, 175, 239));
                break;

            case AppTheme.SolarizedDark:
                SetBrushColor(resources, "Background", Color.FromRgb(0, 43, 54));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(88, 110, 117));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(131, 148, 150));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(147, 161, 161));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(101, 123, 131));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(38, 139, 210));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(0, 43, 54));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(16, 66, 79));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(38, 139, 210));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(88, 110, 117));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(16, 66, 79));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(181, 137, 0));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(88, 110, 117));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(88, 110, 117));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(88, 110, 117));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(101, 123, 131));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(42, 161, 152));
                break;

            case AppTheme.SolarizedLight:
                SetBrushColor(resources, "Background", Color.FromRgb(253, 246, 227));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(238, 232, 213));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(238, 232, 213));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(147, 161, 161));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(88, 110, 117));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(101, 123, 131));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(131, 148, 150));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(38, 139, 210));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(253, 246, 227));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(238, 232, 213));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(245, 239, 220));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(38, 139, 210));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(255, 251, 236));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(147, 161, 161));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(255, 251, 236));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(238, 232, 213));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(255, 251, 236));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(245, 239, 220));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(238, 232, 213));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(181, 137, 0));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(238, 232, 213));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(147, 161, 161));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(255, 251, 236));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(147, 161, 161));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(238, 232, 213));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(147, 161, 161));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(101, 123, 131));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(42, 161, 152));
                break;

            case AppTheme.CatppuccinMocha:
                SetBrushColor(resources, "Background", Color.FromRgb(30, 30, 46));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(24, 24, 37));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(24, 24, 37));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(69, 71, 90));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(205, 214, 244));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(166, 173, 200));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(127, 132, 156));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(203, 166, 247));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(30, 30, 46));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(49, 50, 68));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(69, 71, 90));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(137, 180, 250));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(49, 50, 68));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(69, 71, 90));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(49, 50, 68));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(49, 50, 68));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(49, 50, 68));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(69, 71, 90));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(49, 50, 68));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(249, 226, 175));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(24, 24, 37));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(69, 71, 90));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(24, 24, 37));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(69, 71, 90));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(24, 24, 37));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(108, 112, 134));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(127, 132, 156));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(137, 180, 250));
                break;

            case AppTheme.CatppuccinFrappe:
                SetBrushColor(resources, "Background", Color.FromRgb(48, 52, 70));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(41, 44, 60));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(41, 44, 60));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(81, 87, 109));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(198, 208, 245));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(165, 173, 206));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(131, 139, 167));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(202, 158, 230));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(35, 38, 52));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(65, 69, 89));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(81, 87, 109));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(140, 170, 238));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(41, 44, 60));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(81, 87, 109));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(41, 44, 60));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(41, 44, 60));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(41, 44, 60));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(65, 69, 89));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(65, 69, 89));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(229, 200, 144));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(41, 44, 60));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(81, 87, 109));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(41, 44, 60));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(81, 87, 109));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(41, 44, 60));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(115, 121, 148));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(148, 156, 187));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(140, 170, 238));
                break;

            case AppTheme.CatppuccinMacchiato:
                SetBrushColor(resources, "Background", Color.FromRgb(36, 39, 58));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(30, 32, 48));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(30, 32, 48));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(73, 77, 100));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(202, 211, 245));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(165, 173, 203));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(128, 135, 162));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(198, 160, 246));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(24, 25, 38));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(54, 58, 79));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(73, 77, 100));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(138, 173, 244));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(30, 32, 48));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(73, 77, 100));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(30, 32, 48));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(30, 32, 48));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(30, 32, 48));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(54, 58, 79));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(54, 58, 79));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(238, 212, 159));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(30, 32, 48));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(73, 77, 100));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(30, 32, 48));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(73, 77, 100));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(30, 32, 48));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(110, 115, 141));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(147, 154, 183));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(138, 173, 244));
                break;

            case AppTheme.CatppuccinLatte:
                SetBrushColor(resources, "Background", Color.FromRgb(239, 241, 245));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(230, 233, 239));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(230, 233, 239));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(188, 192, 204));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(76, 79, 105));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(108, 111, 133));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(140, 143, 161));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(136, 57, 239));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(255, 255, 255));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(204, 208, 218));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(188, 192, 204));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(30, 102, 245));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(230, 233, 239));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(188, 192, 204));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(255, 255, 255));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(220, 224, 232));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(255, 255, 255));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(220, 224, 232));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(220, 224, 232));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(223, 142, 29));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(230, 233, 239));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(188, 192, 204));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(255, 255, 255));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(188, 192, 204));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(230, 233, 239));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(156, 160, 176));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(108, 111, 133));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(30, 102, 245));
                break;

            default:
                SetBrushColor(resources, "Background", Color.FromRgb(26, 26, 26));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(36, 36, 36));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(36, 36, 36));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(51, 51, 51));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(232, 232, 232));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(160, 160, 160));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(102, 102, 102));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(59, 130, 246));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(255, 255, 255));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(46, 46, 46));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(51, 51, 51));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(59, 130, 246));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(36, 36, 36));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(51, 51, 51));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(36, 36, 36));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(36, 36, 36));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(36, 36, 36));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(51, 51, 51));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(46, 46, 46));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(234, 179, 8));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(36, 36, 36));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(51, 51, 51));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(46, 46, 46));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(51, 51, 51));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(36, 36, 36));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(102, 102, 102));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(160, 160, 160));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(96, 165, 250));
                break;
        }
    }

    private static void SetBrushColor(ResourceDictionary resources, string key, Color color)
    {
        if (resources.Contains(key) && resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

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
            Console.WriteLine("  AIUsageTracker.UI - DEBUG MODE");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine($"  Started:    {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"  Process ID: {Environment.ProcessId}");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("");
            
            AgentService.LogDiagnostic("AI Usage Tracker UI Debug Mode Enabled");
        }

        base.OnStartup(e);

        if (e.Args.Contains("--test", StringComparer.OrdinalIgnoreCase) &&
            e.Args.Contains("--screenshot", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunHeadlessScreenshotCaptureAsync(e.Args);
            return;
        }
        
        // Apply a safe default immediately; load persisted preferences without blocking startup.
        ApplyTheme(AppTheme.Dark);
        _ = LoadPreferencesAsync();
        
        // Create tray icon
        InitializeTrayIcon();
        
        // Create and show main window
        _mainWindow = new MainWindow();
        _mainWindow.Show();
    }

    private async Task RunHeadlessScreenshotCaptureAsync(string[] args)
    {
        try
        {
            var selectedTheme = AppTheme.Dark;
            var themeArg = GetArgumentValue(args, "--theme");
            if (!string.IsNullOrWhiteSpace(themeArg) && !Enum.TryParse<AppTheme>(themeArg, ignoreCase: true, out selectedTheme))
            {
                throw new ArgumentException($"Unknown theme '{themeArg}'.", nameof(args));
            }

            var isThemeSmokeMode = args.Contains("--theme-smoke", StringComparer.OrdinalIgnoreCase);

            Preferences = new AppPreferences
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
                IsPrivacyMode = true,
                Theme = selectedTheme
            };
            ApplyTheme(Preferences.Theme);
            SetPrivacyMode(true);

            var outputDirectoryArg = GetArgumentValue(args, "--output-dir");
            var screenshotsDir = string.IsNullOrWhiteSpace(outputDirectoryArg)
                ? ResolveScreenshotsDirectory()
                : outputDirectoryArg;
            Directory.CreateDirectory(screenshotsDir);

            if (isThemeSmokeMode)
            {
                var smokeFileName = $"theme_smoke_{selectedTheme.ToString().ToLowerInvariant()}.png";
                await CaptureMainWindowScreenshotAsync(Path.Combine(screenshotsDir, smokeFileName));
                return;
            }

            await CaptureMainWindowScreenshotAsync(Path.Combine(screenshotsDir, "screenshot_dashboard_privacy.png"));
            await CaptureSettingsScreenshotsAsync(screenshotsDir);
            CaptureInfoScreenshot(Path.Combine(screenshotsDir, "screenshot_info_privacy.png"));
        }
        catch (Exception ex)
        {
            AgentService.LogDiagnostic($"Headless screenshot capture failed: {ex}");
            Environment.ExitCode = 1;
        }
        finally
        {
            Shutdown();
        }
    }

    private static string ResolveScreenshotsDirectory()
    {
        var currentDocs = Path.Combine(Environment.CurrentDirectory, "docs");
        if (Directory.Exists(currentDocs))
        {
            return currentDocs;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "docs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return currentDocs;
    }

    internal static void RenderWindowContent(Window window, string outputPath)
    {
        if (window.Content is not FrameworkElement root)
        {
            throw new InvalidOperationException("Window content is not a FrameworkElement.");
        }

        var width = window.Width;
        if (double.IsNaN(width) || width <= 0)
        {
            width = root.Width;
        }
        if (double.IsNaN(width) || width <= 0)
        {
            width = Math.Max(1, root.ActualWidth);
        }
        if (width <= 0)
        {
            width = 380;
        }

        var height = window.Height;
        if (double.IsNaN(height) || height <= 0)
        {
            root.Measure(new Size(width, double.PositiveInfinity));
            height = Math.Max(1, root.DesiredSize.Height);
        }

        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        root.SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Display);
        root.SetValue(TextOptions.TextHintingModeProperty, TextHintingMode.Fixed);
        root.SetValue(TextOptions.TextRenderingModeProperty, TextRenderingMode.ClearType);
        root.SetValue(RenderOptions.ClearTypeHintProperty, ClearTypeHint.Enabled);

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * ScreenshotScaleFactor));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * ScreenshotScaleFactor));
        var backgroundBrush = window.Background is SolidColorBrush solidBackground
            ? new SolidColorBrush(Color.FromRgb(solidBackground.Color.R, solidBackground.Color.G, solidBackground.Color.B))
            : Brushes.Black;
        backgroundBrush.Freeze();

        var contentBitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, ScreenshotDpi, ScreenshotDpi, PixelFormats.Pbgra32);
        contentBitmap.Render(root);
        contentBitmap.Freeze();

        var composedVisual = new DrawingVisual();
        using (var dc = composedVisual.RenderOpen())
        {
            dc.DrawRectangle(backgroundBrush, null, new Rect(0, 0, width, height));
            dc.DrawImage(contentBitmap, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, ScreenshotDpi, ScreenshotDpi, PixelFormats.Pbgra32);
        bitmap.Render(composedVisual);
        bitmap.Freeze();

        var opaqueBitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
        opaqueBitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(opaqueBitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private async Task CaptureMainWindowScreenshotAsync(string outputPath)
    {
        var window = new MainWindow();
        try
        {
            await window.PrepareForHeadlessScreenshotAsync(deterministic: true);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            RenderWindowContent(window, outputPath);
        }
        finally
        {
            window.Close();
        }
    }

    private async Task CaptureSettingsScreenshotsAsync(string outputDirectory)
    {
        var window = new SettingsWindow();
        try
        {
            await window.CaptureHeadlessTabScreenshotsAsync(outputDirectory);
        }
        finally
        {
            window.Close();
        }
    }

    private static void CaptureInfoScreenshot(string outputPath)
    {
        var window = new InfoDialog();
        try
        {
            window.PrepareForHeadlessScreenshot();
            window.UpdateLayout();
            RenderWindowContent(window, outputPath);
        }
        finally
        {
            window.Close();
        }
    }

    private void InitializeTrayIcon()
    {
        // Create context menu
        var contextMenu = new ContextMenu();
        
        // Show menu item
        var showMenuItem = new MenuItem { Header = "Show" };
        showMenuItem.Click += (s, e) => ShowMainWindow();
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
        var trayIconPath = ResolveTrayIconPath();
        var trayIcon = File.Exists(trayIconPath)
            ? new System.Drawing.Icon(trayIconPath)
            : System.Drawing.SystemIcons.Application;

        _trayIcon = new TaskbarIcon
        {
            Icon = trayIcon,
            ToolTipText = "AI Usage Tracker",
            ContextMenu = contextMenu,
            DoubleClickCommand = new RelayCommand(() =>
            {
                ShowMainWindow();
            })
        };

        if (!File.Exists(trayIconPath))
        {
            AgentService.LogDiagnostic($"Tray icon not found at expected paths. Falling back to system icon. Tried: {trayIconPath}");
        }
    }

    private static string? GetArgumentValue(IReadOnlyList<string> args, string argumentName)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], argumentName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string ResolveTrayIconPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "app_icon.ico"),
            Path.Combine(Environment.CurrentDirectory, "Assets", "app_icon.ico"),
            Path.Combine(Environment.CurrentDirectory, "AIUsageTracker.UI.Slim", "Assets", "app_icon.ico")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            return;
        }

        _mainWindow.ShowAndActivate();
    }

    public void UpdateProviderTrayIcons(List<ProviderUsage> usages, List<ProviderConfig> configs, AppPreferences? prefs = null)
    {
        var desiredIcons = new Dictionary<string, (string ToolTip, double Percentage, bool IsQuota)>(StringComparer.OrdinalIgnoreCase);
        var yellowThreshold = prefs?.ColorThresholdYellow ?? 60;
        var redThreshold = prefs?.ColorThresholdRed ?? 80;
        var invert = prefs?.InvertProgressBar ?? false;

        foreach (var config in configs)
        {
            var usage = usages.FirstOrDefault(u => u.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (usage == null)
            {
                continue;
            }

            if (config.ShowInTray &&
                usage.IsAvailable &&
                !usage.Description.Contains("unknown", StringComparison.OrdinalIgnoreCase))
            {
                var isQuota = usage.IsQuotaBased || usage.PlanType == PlanType.Coding;
                desiredIcons[config.ProviderId] = ($"{usage.ProviderName}: {usage.Description}", usage.RequestsPercentage, isQuota);
            }

            if (config.EnabledSubTrays == null || usage.Details == null)
            {
                continue;
            }

            foreach (var subName in config.EnabledSubTrays)
            {
                var detail = usage.Details.FirstOrDefault(d => d.Name.Equals(subName, StringComparison.OrdinalIgnoreCase));
                if (detail == null)
                {
                    continue;
                }

                if (!IsSubTrayEligibleDetail(detail))
                {
                    continue;
                }

                var detailPercent = ParsePercent(detail.Used);
                if (!detailPercent.HasValue)
                {
                    continue;
                }

                var key = $"{config.ProviderId}:{subName}";
                var isQuotaSub = usage.IsQuotaBased || usage.PlanType == PlanType.Coding;
                desiredIcons[key] = (
                    $"{usage.ProviderName} - {subName}: {detail.Description} ({detail.Used})",
                    detailPercent.Value,
                    isQuotaSub
                );
            }
        }

        var currentKeys = _providerTrayIcons.Keys.ToList();
        foreach (var key in currentKeys)
        {
            if (desiredIcons.ContainsKey(key))
            {
                continue;
            }

            _providerTrayIcons[key].Dispose();
            _providerTrayIcons.Remove(key);
        }

        foreach (var kvp in desiredIcons)
        {
            var key = kvp.Key;
            var info = kvp.Value;
            var iconSource = GenerateUsageIcon(info.Percentage, yellowThreshold, redThreshold, invert, info.IsQuota);

            if (!_providerTrayIcons.ContainsKey(key))
            {
                var tray = new TaskbarIcon
                {
                    ToolTipText = info.ToolTip,
                    IconSource = iconSource
                };
                tray.TrayLeftMouseDown += (s, e) => ShowMainWindow();
                tray.TrayMouseDoubleClick += (s, e) => ShowMainWindow();
                _providerTrayIcons.Add(key, tray);
            }
            else
            {
                var tray = _providerTrayIcons[key];
                tray.ToolTipText = info.ToolTip;
                tray.IconSource = iconSource;
            }
        }
    }

    private static double? ParsePercent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parsedValue = value.Replace("%", string.Empty).Trim();
        return double.TryParse(parsedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0, Math.Min(100, parsed))
            : null;
    }

    private static bool IsSubTrayEligibleDetail(ProviderUsageDetail detail)
    {
        if (string.IsNullOrWhiteSpace(detail.Name))
        {
            return false;
        }

        return !detail.Name.Contains("window", StringComparison.OrdinalIgnoreCase) &&
               !detail.Name.Contains("credit", StringComparison.OrdinalIgnoreCase);
    }

    private static ImageSource GenerateUsageIcon(double percentage, int yellowThreshold, int redThreshold, bool invert = false, bool isQuota = false)
    {
        var size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 20, 20)), null, new Rect(0, 0, size, size));
            dc.DrawRectangle(null, new Pen(Brushes.DimGray, 1), new Rect(0.5, 0.5, size - 1, size - 1));

            var fillBrush = isQuota
                ? (percentage < (100 - redThreshold)
                    ? Brushes.Crimson
                    : (percentage < (100 - yellowThreshold) ? Brushes.Gold : Brushes.MediumSeaGreen))
                : (percentage > redThreshold
                    ? Brushes.Crimson
                    : (percentage > yellowThreshold ? Brushes.Gold : Brushes.MediumSeaGreen));

            var barWidth = size - 6;
            var barHeight = size - 6;
            double fillHeight;
            if (invert)
            {
                var remaining = Math.Max(0, 100.0 - percentage);
                fillHeight = (remaining / 100.0) * barHeight;
            }
            else
            {
                fillHeight = (percentage / 100.0) * barHeight;
            }

            dc.DrawRectangle(fillBrush, null, new Rect(3, size - 3 - fillHeight, barWidth, fillHeight));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        foreach (var tray in _providerTrayIcons.Values)
        {
            tray.Dispose();
        }
        _providerTrayIcons.Clear();
        base.OnExit(e);
    }

    private static async Task LoadPreferencesAsync()
    {
        try
        {
            Preferences = await UiPreferencesStore.LoadAsync();
            IsPrivacyMode = Preferences.IsPrivacyMode;
            ApplyTheme(Preferences.Theme);
        }
        catch
        {
            // Use defaults
            ApplyTheme(AppTheme.Dark);
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

