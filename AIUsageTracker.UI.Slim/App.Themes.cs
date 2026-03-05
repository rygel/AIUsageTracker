using System.Windows;
using System.Windows.Media;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

public partial class App
{
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
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(82, 82, 82));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(115, 115, 115));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(165, 180, 252));
                break;

            case AppTheme.Dracula:
                SetBrushColor(resources, "Background", Color.FromRgb(40, 42, 54));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(33, 34, 44));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(33, 34, 44));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(68, 71, 90));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(248, 248, 242));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(139, 233, 253));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(98, 114, 164));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(255, 121, 198));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(40, 42, 54));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(68, 71, 90));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(98, 114, 164));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(189, 147, 249));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(33, 34, 44));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(68, 71, 90));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(33, 34, 44));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(68, 71, 90));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(33, 34, 44));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(68, 71, 90));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(68, 71, 90));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(241, 250, 140));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(33, 34, 44));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(68, 71, 90));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(33, 34, 44));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(68, 71, 90));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(33, 34, 44));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(98, 114, 164));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(139, 233, 253));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(139, 233, 253));
                break;

            case AppTheme.Nord:
                SetBrushColor(resources, "Background", Color.FromRgb(46, 52, 64));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(76, 86, 106));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(236, 239, 244));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(216, 222, 233));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(129, 161, 193));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(136, 192, 208));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(46, 52, 64));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(67, 76, 94));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(76, 86, 106));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(136, 192, 208));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(76, 86, 106));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(67, 76, 94));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(67, 76, 94));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(67, 76, 94));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(235, 203, 139));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(76, 86, 106));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(76, 86, 106));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(59, 66, 82));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(76, 86, 106));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(129, 161, 193));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(136, 192, 208));
                break;

            case AppTheme.Monokai:
                SetBrushColor(resources, "Background", Color.FromRgb(39, 40, 34));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(30, 31, 28));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(30, 31, 28));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(73, 72, 62));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(248, 248, 242));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(166, 226, 46));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(117, 113, 94));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(249, 38, 114));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(248, 248, 242));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(73, 72, 62));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(117, 113, 94));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(249, 38, 114));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(30, 31, 28));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(73, 72, 62));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(30, 31, 28));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(73, 72, 62));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(30, 31, 28));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(73, 72, 62));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(73, 72, 62));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(230, 219, 116));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(30, 31, 28));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(73, 72, 62));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(30, 31, 28));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(73, 72, 62));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(30, 31, 28));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(117, 113, 94));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(166, 226, 46));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(102, 217, 239));
                break;

            case AppTheme.OneDark:
                SetBrushColor(resources, "Background", Color.FromRgb(40, 44, 52));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(24, 26, 31));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(171, 178, 191));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(152, 195, 121));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(92, 99, 112));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(97, 175, 239));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(255, 255, 255));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(53, 59, 69));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(62, 68, 81));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(97, 175, 239));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(24, 26, 31));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(53, 59, 69));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(53, 59, 69));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(53, 59, 69));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(229, 192, 123));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(24, 26, 31));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "CardBorder", Color.FromRgb(24, 26, 31));
                SetBrushColor(resources, "ScrollBarBackground", Color.FromRgb(33, 37, 43));
                SetBrushColor(resources, "ScrollBarForeground", Color.FromRgb(75, 82, 99));
                SetBrushColor(resources, "ScrollBarHover", Color.FromRgb(92, 99, 112));
                SetBrushColor(resources, "LinkForeground", Color.FromRgb(97, 175, 239));
                break;

            case AppTheme.SolarizedDark:
                SetBrushColor(resources, "Background", Color.FromRgb(0, 43, 54));
                SetBrushColor(resources, "HeaderBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "FooterBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "BorderColor", Color.FromRgb(88, 110, 117));
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(147, 161, 161));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(131, 148, 150));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(101, 123, 131));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(38, 139, 210));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(253, 246, 227));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(88, 110, 117));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(38, 139, 210));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(0, 43, 54));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(88, 110, 117));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(7, 54, 66));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(88, 110, 117));

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
                SetBrushColor(resources, "PrimaryText", Color.FromRgb(101, 123, 131));
                SetBrushColor(resources, "SecondaryText", Color.FromRgb(88, 110, 117));
                SetBrushColor(resources, "TertiaryText", Color.FromRgb(147, 161, 161));
                SetBrushColor(resources, "AccentColor", Color.FromRgb(38, 139, 210));
                SetBrushColor(resources, "AccentForeground", Color.FromRgb(253, 246, 227));

                SetBrushColor(resources, "ButtonBackground", Color.FromRgb(238, 232, 213));
                SetBrushColor(resources, "ButtonHover", Color.FromRgb(147, 161, 161));
                SetBrushColor(resources, "ButtonPressed", Color.FromRgb(38, 139, 210));
                SetBrushColor(resources, "ControlBackground", Color.FromRgb(253, 246, 227));
                SetBrushColor(resources, "ControlBorder", Color.FromRgb(147, 161, 161));
                SetBrushColor(resources, "InputBackground", Color.FromRgb(238, 232, 213));
                SetBrushColor(resources, "TabUnselected", Color.FromRgb(238, 232, 213));
                SetBrushColor(resources, "ComboBoxBackground", Color.FromRgb(238, 232, 213));
                SetBrushColor(resources, "ComboBoxItemHover", Color.FromRgb(147, 161, 161));

                SetBrushColor(resources, "ProgressBarBackground", Color.FromRgb(238, 232, 213));
                SetBrushColor(resources, "StatusTextWarning", Color.FromRgb(181, 137, 0));
                SetBrushColor(resources, "GroupHeaderBackground", Color.FromRgb(238, 232, 213));
                SetBrushColor(resources, "GroupHeaderBorder", Color.FromRgb(147, 161, 161));
                SetBrushColor(resources, "CardBackground", Color.FromRgb(238, 232, 213));
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

            case AppTheme.Dark:
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

    public static void ApplyTheme(Window window)
    {
        ApplyTheme(Preferences.Theme);
    }

    public static void ApplyTheme(Window window, string themeName)
    {
        if (Enum.TryParse<AppTheme>(themeName, true, out var theme))
        {
            ApplyTheme(theme);
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
}
