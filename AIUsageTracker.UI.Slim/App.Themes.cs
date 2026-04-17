// <copyright file="App.Themes.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Media;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

public partial class App
{
    private const string KeyAccentColor = "AccentColor";
    private const string KeyAccentForeground = "AccentForeground";
    private const string KeyBackground = "Background";
    private const string KeyBorderColor = "BorderColor";
    private const string KeyButtonBackground = "ButtonBackground";
    private const string KeyButtonHover = "ButtonHover";
    private const string KeyButtonPressed = "ButtonPressed";
    private const string KeyCardBackground = "CardBackground";
    private const string KeyCardBorder = "CardBorder";
    private const string KeyComboBoxBackground = "ComboBoxBackground";
    private const string KeyComboBoxItemHover = "ComboBoxItemHover";
    private const string KeyControlBackground = "ControlBackground";
    private const string KeyControlBorder = "ControlBorder";
    private const string KeyFooterBackground = "FooterBackground";
    private const string KeyGroupHeaderBackground = "GroupHeaderBackground";
    private const string KeyGroupHeaderBorder = "GroupHeaderBorder";
    private const string KeyHeaderBackground = "HeaderBackground";
    private const string KeyInputBackground = "InputBackground";
    private const string KeyLinkForeground = "LinkForeground";
    private const string KeyPrimaryText = "PrimaryText";
    private const string KeyProgressBarBackground = "ProgressBarBackground";
    private const string KeyScrollBarBackground = "ScrollBarBackground";
    private const string KeyScrollBarForeground = "ScrollBarForeground";
    private const string KeyScrollBarHover = "ScrollBarHover";
    private const string KeySecondaryText = "SecondaryText";
    private const string KeyStatusTextWarning = "StatusTextWarning";
    private const string KeyTabUnselected = "TabUnselected";
    private const string KeyTertiaryText = "TertiaryText";
#pragma warning disable MA0051 // Theme application is intentionally a single palette table.
    private static readonly Dictionary<AppTheme, Dictionary<string, Color>> ThemePalettes = new()
    {
        [AppTheme.Light] = new(StringComparer.Ordinal)
        {
            [KeyBackground] = Color.FromRgb(245, 245, 245),
            [KeyHeaderBackground] = Color.FromRgb(229, 229, 229),
            [KeyFooterBackground] = Color.FromRgb(229, 229, 229),
            [KeyBorderColor] = Color.FromRgb(221, 221, 221),
            [KeyPrimaryText] = Color.FromRgb(26, 26, 26),
            [KeySecondaryText] = Color.FromRgb(82, 82, 82),
            [KeyTertiaryText] = Color.FromRgb(136, 136, 136),
            [KeyAccentColor] = Color.FromRgb(37, 99, 235),
            [KeyAccentForeground] = Color.FromRgb(255, 255, 255),
            [KeyButtonBackground] = Color.FromRgb(229, 229, 229),
            [KeyButtonHover] = Color.FromRgb(238, 238, 238),
            [KeyButtonPressed] = Color.FromRgb(37, 99, 235),
            [KeyControlBackground] = Color.FromRgb(255, 255, 255),
            [KeyControlBorder] = Color.FromRgb(221, 221, 221),
            [KeyInputBackground] = Color.FromRgb(255, 255, 255),
            [KeyTabUnselected] = Color.FromRgb(229, 229, 229),
            [KeyComboBoxBackground] = Color.FromRgb(255, 255, 255),
            [KeyComboBoxItemHover] = Color.FromRgb(238, 238, 238),
            [KeyProgressBarBackground] = Color.FromRgb(229, 229, 229),
            [KeyStatusTextWarning] = Color.FromRgb(202, 138, 4),
            [KeyGroupHeaderBackground] = Color.FromRgb(229, 229, 229),
            [KeyGroupHeaderBorder] = Color.FromRgb(221, 221, 221),
            [KeyCardBackground] = Color.FromRgb(255, 255, 255),
            [KeyCardBorder] = Color.FromRgb(221, 221, 221),
            [KeyScrollBarBackground] = Color.FromRgb(229, 229, 229),
            [KeyScrollBarForeground] = Color.FromRgb(170, 170, 170),
            [KeyScrollBarHover] = Color.FromRgb(140, 140, 140),
            [KeyLinkForeground] = Color.FromRgb(37, 99, 235),
        },
        [AppTheme.Corporate] = new(StringComparer.Ordinal)
        {
            [KeyBackground] = Color.FromRgb(15, 23, 42),
            [KeyHeaderBackground] = Color.FromRgb(30, 41, 59),
            [KeyFooterBackground] = Color.FromRgb(30, 41, 59),
            [KeyBorderColor] = Color.FromRgb(51, 65, 85),
            [KeyPrimaryText] = Color.FromRgb(241, 245, 249),
            [KeySecondaryText] = Color.FromRgb(148, 163, 184),
            [KeyTertiaryText] = Color.FromRgb(100, 116, 139),
            [KeyAccentColor] = Color.FromRgb(59, 130, 246),
            [KeyAccentForeground] = Color.FromRgb(255, 255, 255),
            [KeyButtonBackground] = Color.FromRgb(51, 65, 85),
            [KeyButtonHover] = Color.FromRgb(61, 79, 102),
            [KeyButtonPressed] = Color.FromRgb(59, 130, 246),
            [KeyControlBackground] = Color.FromRgb(30, 41, 59),
            [KeyControlBorder] = Color.FromRgb(51, 65, 85),
            [KeyInputBackground] = Color.FromRgb(30, 41, 59),
            [KeyTabUnselected] = Color.FromRgb(51, 65, 85),
            [KeyComboBoxBackground] = Color.FromRgb(30, 41, 59),
            [KeyComboBoxItemHover] = Color.FromRgb(61, 79, 102),
            [KeyProgressBarBackground] = Color.FromRgb(51, 65, 85),
            [KeyStatusTextWarning] = Color.FromRgb(245, 158, 11),
            [KeyGroupHeaderBackground] = Color.FromRgb(30, 41, 59),
            [KeyGroupHeaderBorder] = Color.FromRgb(51, 65, 85),
            [KeyCardBackground] = Color.FromRgb(30, 41, 59),
            [KeyCardBorder] = Color.FromRgb(51, 65, 85),
            [KeyScrollBarBackground] = Color.FromRgb(30, 41, 59),
            [KeyScrollBarForeground] = Color.FromRgb(100, 116, 139),
            [KeyScrollBarHover] = Color.FromRgb(148, 163, 184),
            [KeyLinkForeground] = Color.FromRgb(96, 165, 250),
        },
        [AppTheme.Midnight] = new(StringComparer.Ordinal)
        {
            [KeyBackground] = Color.FromRgb(10, 10, 10),
            [KeyHeaderBackground] = Color.FromRgb(20, 20, 20),
            [KeyFooterBackground] = Color.FromRgb(20, 20, 20),
            [KeyBorderColor] = Color.FromRgb(38, 38, 38),
            [KeyPrimaryText] = Color.FromRgb(250, 250, 250),
            [KeySecondaryText] = Color.FromRgb(136, 136, 136),
            [KeyTertiaryText] = Color.FromRgb(85, 85, 85),
            [KeyAccentColor] = Color.FromRgb(129, 140, 248),
            [KeyAccentForeground] = Color.FromRgb(255, 255, 255),
            [KeyButtonBackground] = Color.FromRgb(31, 31, 31),
            [KeyButtonHover] = Color.FromRgb(42, 42, 42),
            [KeyButtonPressed] = Color.FromRgb(129, 140, 248),
            [KeyControlBackground] = Color.FromRgb(20, 20, 20),
            [KeyControlBorder] = Color.FromRgb(38, 38, 38),
            [KeyInputBackground] = Color.FromRgb(20, 20, 20),
            [KeyTabUnselected] = Color.FromRgb(31, 31, 31),
            [KeyComboBoxBackground] = Color.FromRgb(20, 20, 20),
            [KeyComboBoxItemHover] = Color.FromRgb(42, 42, 42),
            [KeyProgressBarBackground] = Color.FromRgb(31, 31, 31),
            [KeyStatusTextWarning] = Color.FromRgb(251, 191, 36),
            [KeyGroupHeaderBackground] = Color.FromRgb(20, 20, 20),
            [KeyGroupHeaderBorder] = Color.FromRgb(38, 38, 38),
            [KeyCardBackground] = Color.FromRgb(20, 20, 20),
            [KeyCardBorder] = Color.FromRgb(38, 38, 38),
            [KeyScrollBarBackground] = Color.FromRgb(20, 20, 20),
            [KeyScrollBarForeground] = Color.FromRgb(82, 82, 82),
            [KeyScrollBarHover] = Color.FromRgb(115, 115, 115),
            [KeyLinkForeground] = Color.FromRgb(165, 180, 252),
        },
        [AppTheme.Dracula] = new(StringComparer.Ordinal)
        {
            [KeyBackground] = Color.FromRgb(40, 42, 54),
            [KeyHeaderBackground] = Color.FromRgb(33, 34, 44),
            [KeyFooterBackground] = Color.FromRgb(33, 34, 44),
            [KeyBorderColor] = Color.FromRgb(68, 71, 90),
            [KeyPrimaryText] = Color.FromRgb(248, 248, 242),
            [KeySecondaryText] = Color.FromRgb(139, 233, 253),
            [KeyTertiaryText] = Color.FromRgb(98, 114, 164),
            [KeyAccentColor] = Color.FromRgb(255, 121, 198),
            [KeyAccentForeground] = Color.FromRgb(40, 42, 54),
            [KeyButtonBackground] = Color.FromRgb(68, 71, 90),
            [KeyButtonHover] = Color.FromRgb(98, 114, 164),
            [KeyButtonPressed] = Color.FromRgb(189, 147, 249),
            [KeyControlBackground] = Color.FromRgb(33, 34, 44),
            [KeyControlBorder] = Color.FromRgb(68, 71, 90),
            [KeyInputBackground] = Color.FromRgb(33, 34, 44),
            [KeyTabUnselected] = Color.FromRgb(68, 71, 90),
            [KeyComboBoxBackground] = Color.FromRgb(33, 34, 44),
            [KeyComboBoxItemHover] = Color.FromRgb(68, 71, 90),
            [KeyProgressBarBackground] = Color.FromRgb(68, 71, 90),
            [KeyStatusTextWarning] = Color.FromRgb(241, 250, 140),
            [KeyGroupHeaderBackground] = Color.FromRgb(33, 34, 44),
            [KeyGroupHeaderBorder] = Color.FromRgb(68, 71, 90),
            [KeyCardBackground] = Color.FromRgb(33, 34, 44),
            [KeyCardBorder] = Color.FromRgb(68, 71, 90),
            [KeyScrollBarBackground] = Color.FromRgb(33, 34, 44),
            [KeyScrollBarForeground] = Color.FromRgb(98, 114, 164),
            [KeyScrollBarHover] = Color.FromRgb(139, 233, 253),
            [KeyLinkForeground] = Color.FromRgb(139, 233, 253),
        },
        [AppTheme.Nord] = new(StringComparer.Ordinal)
        {
            [KeyBackground] = Color.FromRgb(46, 52, 64),
            [KeyHeaderBackground] = Color.FromRgb(59, 66, 82),
            [KeyFooterBackground] = Color.FromRgb(59, 66, 82),
            [KeyBorderColor] = Color.FromRgb(76, 86, 106),
            [KeyPrimaryText] = Color.FromRgb(236, 239, 244),
            [KeySecondaryText] = Color.FromRgb(216, 222, 233),
            [KeyTertiaryText] = Color.FromRgb(129, 161, 193),
            [KeyAccentColor] = Color.FromRgb(136, 192, 208),
            [KeyAccentForeground] = Color.FromRgb(46, 52, 64),
            [KeyButtonBackground] = Color.FromRgb(67, 76, 94),
            [KeyButtonHover] = Color.FromRgb(76, 86, 106),
            [KeyButtonPressed] = Color.FromRgb(136, 192, 208),
            [KeyControlBackground] = Color.FromRgb(59, 66, 82),
            [KeyControlBorder] = Color.FromRgb(76, 86, 106),
            [KeyInputBackground] = Color.FromRgb(59, 66, 82),
            [KeyTabUnselected] = Color.FromRgb(67, 76, 94),
            [KeyComboBoxBackground] = Color.FromRgb(59, 66, 82),
            [KeyComboBoxItemHover] = Color.FromRgb(67, 76, 94),
            [KeyProgressBarBackground] = Color.FromRgb(67, 76, 94),
            [KeyStatusTextWarning] = Color.FromRgb(235, 203, 139),
            [KeyGroupHeaderBackground] = Color.FromRgb(59, 66, 82),
            [KeyGroupHeaderBorder] = Color.FromRgb(76, 86, 106),
            [KeyCardBackground] = Color.FromRgb(59, 66, 82),
            [KeyCardBorder] = Color.FromRgb(76, 86, 106),
            [KeyScrollBarBackground] = Color.FromRgb(59, 66, 82),
            [KeyScrollBarForeground] = Color.FromRgb(76, 86, 106),
            [KeyScrollBarHover] = Color.FromRgb(129, 161, 193),
            [KeyLinkForeground] = Color.FromRgb(136, 192, 208),
        },
        [AppTheme.Monokai] = new(StringComparer.Ordinal)
        {
            [KeyBackground] = Color.FromRgb(39, 40, 34),
            [KeyHeaderBackground] = Color.FromRgb(30, 31, 28),
            [KeyFooterBackground] = Color.FromRgb(30, 31, 28),
            [KeyBorderColor] = Color.FromRgb(73, 72, 62),
            [KeyPrimaryText] = Color.FromRgb(248, 248, 242),
            [KeySecondaryText] = Color.FromRgb(166, 226, 46),
            [KeyTertiaryText] = Color.FromRgb(117, 113, 94),
            [KeyAccentColor] = Color.FromRgb(249, 38, 114),
            [KeyAccentForeground] = Color.FromRgb(248, 248, 242),
            [KeyButtonBackground] = Color.FromRgb(73, 72, 62),
            [KeyButtonHover] = Color.FromRgb(117, 113, 94),
            [KeyButtonPressed] = Color.FromRgb(249, 38, 114),
            [KeyControlBackground] = Color.FromRgb(30, 31, 28),
            [KeyControlBorder] = Color.FromRgb(73, 72, 62),
            [KeyInputBackground] = Color.FromRgb(30, 31, 28),
            [KeyTabUnselected] = Color.FromRgb(73, 72, 62),
            [KeyComboBoxBackground] = Color.FromRgb(30, 31, 28),
            [KeyComboBoxItemHover] = Color.FromRgb(73, 72, 62),
            [KeyProgressBarBackground] = Color.FromRgb(73, 72, 62),
            [KeyStatusTextWarning] = Color.FromRgb(230, 219, 116),
            [KeyGroupHeaderBackground] = Color.FromRgb(30, 31, 28),
            [KeyGroupHeaderBorder] = Color.FromRgb(73, 72, 62),
            [KeyCardBackground] = Color.FromRgb(30, 31, 28),
            [KeyCardBorder] = Color.FromRgb(73, 72, 62),
            [KeyScrollBarBackground] = Color.FromRgb(30, 31, 28),
            [KeyScrollBarForeground] = Color.FromRgb(117, 113, 94),
            [KeyScrollBarHover] = Color.FromRgb(166, 226, 46),
            [KeyLinkForeground] = Color.FromRgb(102, 217, 239),
        },
        [AppTheme.OneDark] = new(StringComparer.Ordinal)
        {
            [KeyBackground] = Color.FromRgb(40, 44, 52),
            [KeyHeaderBackground] = Color.FromRgb(33, 37, 43),
            [KeyFooterBackground] = Color.FromRgb(33, 37, 43),
            [KeyBorderColor] = Color.FromRgb(24, 26, 31),
            [KeyPrimaryText] = Color.FromRgb(171, 178, 191),
            [KeySecondaryText] = Color.FromRgb(152, 195, 121),
            [KeyTertiaryText] = Color.FromRgb(92, 99, 112),
            [KeyAccentColor] = Color.FromRgb(97, 175, 239),
            [KeyAccentForeground] = Color.FromRgb(255, 255, 255),
            [KeyButtonBackground] = Color.FromRgb(53, 59, 69),
            [KeyButtonHover] = Color.FromRgb(62, 68, 81),
            [KeyButtonPressed] = Color.FromRgb(97, 175, 239),
            [KeyControlBackground] = Color.FromRgb(33, 37, 43),
            [KeyControlBorder] = Color.FromRgb(24, 26, 31),
            [KeyInputBackground] = Color.FromRgb(33, 37, 43),
            [KeyTabUnselected] = Color.FromRgb(53, 59, 69),
            [KeyComboBoxBackground] = Color.FromRgb(33, 37, 43),
            [KeyComboBoxItemHover] = Color.FromRgb(53, 59, 69),
            [KeyProgressBarBackground] = Color.FromRgb(53, 59, 69),
            [KeyStatusTextWarning] = Color.FromRgb(229, 192, 123),
            [KeyGroupHeaderBackground] = Color.FromRgb(33, 37, 43),
            [KeyGroupHeaderBorder] = Color.FromRgb(24, 26, 31),
            [KeyCardBackground] = Color.FromRgb(33, 37, 43),
            [KeyCardBorder] = Color.FromRgb(24, 26, 31),
            [KeyScrollBarBackground] = Color.FromRgb(33, 37, 43),
            [KeyScrollBarForeground] = Color.FromRgb(75, 82, 99),
            [KeyScrollBarHover] = Color.FromRgb(92, 99, 112),
            [KeyLinkForeground] = Color.FromRgb(97, 175, 239),
        },
        [AppTheme.SolarizedDark] = new(StringComparer.Ordinal)
        {
            [KeyBackground] = Color.FromRgb(0, 43, 54),
            [KeyHeaderBackground] = Color.FromRgb(7, 54, 66),
            [KeyFooterBackground] = Color.FromRgb(7, 54, 66),
            [KeyBorderColor] = Color.FromRgb(88, 110, 117),
            [KeyPrimaryText] = Color.FromRgb(147, 161, 161),
            [KeySecondaryText] = Color.FromRgb(131, 148, 150),
            [KeyTertiaryText] = Color.FromRgb(101, 123, 131),
            [KeyAccentColor] = Color.FromRgb(38, 139, 210),
            [KeyAccentForeground] = Color.FromRgb(253, 246, 227),
            [KeyButtonBackground] = Color.FromRgb(7, 54, 66),
            [KeyButtonHover] = Color.FromRgb(88, 110, 117),
            [KeyButtonPressed] = Color.FromRgb(38, 139, 210),
            [KeyControlBackground] = Color.FromRgb(0, 43, 54),
            [KeyControlBorder] = Color.FromRgb(88, 110, 117),
            [KeyInputBackground] = Color.FromRgb(7, 54, 66),
            [KeyTabUnselected] = Color.FromRgb(7, 54, 66),
            [KeyComboBoxBackground] = Color.FromRgb(7, 54, 66),
            [KeyComboBoxItemHover] = Color.FromRgb(88, 110, 117),
            [KeyProgressBarBackground] = Color.FromRgb(7, 54, 66),
            [KeyStatusTextWarning] = Color.FromRgb(181, 137, 0),
            [KeyGroupHeaderBackground] = Color.FromRgb(7, 54, 66),
            [KeyGroupHeaderBorder] = Color.FromRgb(88, 110, 117),
            [KeyCardBackground] = Color.FromRgb(7, 54, 66),
            [KeyCardBorder] = Color.FromRgb(88, 110, 117),
            [KeyScrollBarBackground] = Color.FromRgb(7, 54, 66),
            [KeyScrollBarForeground] = Color.FromRgb(88, 110, 117),
            [KeyScrollBarHover] = Color.FromRgb(101, 123, 131),
            [KeyLinkForeground] = Color.FromRgb(42, 161, 152),
        },
        [AppTheme.SolarizedLight] = new(StringComparer.Ordinal)
        {
            [KeyBackground] = Color.FromRgb(253, 246, 227),
            [KeyHeaderBackground] = Color.FromRgb(238, 232, 213),
            [KeyFooterBackground] = Color.FromRgb(238, 232, 213),
            [KeyBorderColor] = Color.FromRgb(147, 161, 161),
            [KeyPrimaryText] = Color.FromRgb(101, 123, 131),
            [KeySecondaryText] = Color.FromRgb(88, 110, 117),
            [KeyTertiaryText] = Color.FromRgb(147, 161, 161),
            [KeyAccentColor] = Color.FromRgb(38, 139, 210),
            [KeyAccentForeground] = Color.FromRgb(253, 246, 227),
            [KeyButtonBackground] = Color.FromRgb(238, 232, 213),
            [KeyButtonHover] = Color.FromRgb(147, 161, 161),
            [KeyButtonPressed] = Color.FromRgb(38, 139, 210),
            [KeyControlBackground] = Color.FromRgb(253, 246, 227),
            [KeyControlBorder] = Color.FromRgb(147, 161, 161),
            [KeyInputBackground] = Color.FromRgb(238, 232, 213),
            [KeyTabUnselected] = Color.FromRgb(238, 232, 213),
            [KeyComboBoxBackground] = Color.FromRgb(238, 232, 213),
            [KeyComboBoxItemHover] = Color.FromRgb(147, 161, 161),
            [KeyProgressBarBackground] = Color.FromRgb(238, 232, 213),
            [KeyStatusTextWarning] = Color.FromRgb(181, 137, 0),
            [KeyGroupHeaderBackground] = Color.FromRgb(238, 232, 213),
            [KeyGroupHeaderBorder] = Color.FromRgb(147, 161, 161),
            [KeyCardBackground] = Color.FromRgb(238, 232, 213),
            [KeyCardBorder] = Color.FromRgb(147, 161, 161),
            [KeyScrollBarBackground] = Color.FromRgb(238, 232, 213),
            [KeyScrollBarForeground] = Color.FromRgb(147, 161, 161),
            [KeyScrollBarHover] = Color.FromRgb(101, 123, 131),
            [KeyLinkForeground] = Color.FromRgb(42, 161, 152),
        },
        [AppTheme.CatppuccinMocha] = new(StringComparer.Ordinal)
        {
            [KeyBackground] = Color.FromRgb(30, 30, 46),
            [KeyHeaderBackground] = Color.FromRgb(24, 24, 37),
            [KeyFooterBackground] = Color.FromRgb(24, 24, 37),
            [KeyBorderColor] = Color.FromRgb(69, 71, 90),
            [KeyPrimaryText] = Color.FromRgb(205, 214, 244),
            [KeySecondaryText] = Color.FromRgb(166, 173, 200),
            [KeyTertiaryText] = Color.FromRgb(127, 132, 156),
            [KeyAccentColor] = Color.FromRgb(203, 166, 247),
            [KeyAccentForeground] = Color.FromRgb(30, 30, 46),
            [KeyButtonBackground] = Color.FromRgb(49, 50, 68),
            [KeyButtonHover] = Color.FromRgb(69, 71, 90),
            [KeyButtonPressed] = Color.FromRgb(137, 180, 250),
            [KeyControlBackground] = Color.FromRgb(49, 50, 68),
            [KeyControlBorder] = Color.FromRgb(69, 71, 90),
            [KeyInputBackground] = Color.FromRgb(49, 50, 68),
            [KeyTabUnselected] = Color.FromRgb(49, 50, 68),
            [KeyComboBoxBackground] = Color.FromRgb(49, 50, 68),
            [KeyComboBoxItemHover] = Color.FromRgb(69, 71, 90),
            [KeyProgressBarBackground] = Color.FromRgb(49, 50, 68),
            [KeyStatusTextWarning] = Color.FromRgb(249, 226, 175),
            [KeyGroupHeaderBackground] = Color.FromRgb(24, 24, 37),
            [KeyGroupHeaderBorder] = Color.FromRgb(69, 71, 90),
            [KeyCardBackground] = Color.FromRgb(24, 24, 37),
            [KeyCardBorder] = Color.FromRgb(69, 71, 90),
            [KeyScrollBarBackground] = Color.FromRgb(24, 24, 37),
            [KeyScrollBarForeground] = Color.FromRgb(108, 112, 134),
            [KeyScrollBarHover] = Color.FromRgb(127, 132, 156),
            [KeyLinkForeground] = Color.FromRgb(137, 180, 250),
        },
        [AppTheme.CatppuccinFrappe] = new(StringComparer.Ordinal)
        {
            [KeyBackground] = Color.FromRgb(48, 52, 70),
            [KeyHeaderBackground] = Color.FromRgb(41, 44, 60),
            [KeyFooterBackground] = Color.FromRgb(41, 44, 60),
            [KeyBorderColor] = Color.FromRgb(81, 87, 109),
            [KeyPrimaryText] = Color.FromRgb(198, 208, 245),
            [KeySecondaryText] = Color.FromRgb(165, 173, 206),
            [KeyTertiaryText] = Color.FromRgb(131, 139, 167),
            [KeyAccentColor] = Color.FromRgb(202, 158, 230),
            [KeyAccentForeground] = Color.FromRgb(35, 38, 52),
            [KeyButtonBackground] = Color.FromRgb(65, 69, 89),
            [KeyButtonHover] = Color.FromRgb(81, 87, 109),
            [KeyButtonPressed] = Color.FromRgb(140, 170, 238),
            [KeyControlBackground] = Color.FromRgb(41, 44, 60),
            [KeyControlBorder] = Color.FromRgb(81, 87, 109),
            [KeyInputBackground] = Color.FromRgb(41, 44, 60),
            [KeyTabUnselected] = Color.FromRgb(41, 44, 60),
            [KeyComboBoxBackground] = Color.FromRgb(41, 44, 60),
            [KeyComboBoxItemHover] = Color.FromRgb(65, 69, 89),
            [KeyProgressBarBackground] = Color.FromRgb(65, 69, 89),
            [KeyStatusTextWarning] = Color.FromRgb(229, 200, 144),
            [KeyGroupHeaderBackground] = Color.FromRgb(41, 44, 60),
            [KeyGroupHeaderBorder] = Color.FromRgb(81, 87, 109),
            [KeyCardBackground] = Color.FromRgb(41, 44, 60),
            [KeyCardBorder] = Color.FromRgb(81, 87, 109),
            [KeyScrollBarBackground] = Color.FromRgb(41, 44, 60),
            [KeyScrollBarForeground] = Color.FromRgb(115, 121, 148),
            [KeyScrollBarHover] = Color.FromRgb(148, 156, 187),
            [KeyLinkForeground] = Color.FromRgb(140, 170, 238),
        },
        [AppTheme.CatppuccinMacchiato] = new(StringComparer.Ordinal)
        {
            [KeyBackground] = Color.FromRgb(36, 39, 58),
            [KeyHeaderBackground] = Color.FromRgb(30, 32, 48),
            [KeyFooterBackground] = Color.FromRgb(30, 32, 48),
            [KeyBorderColor] = Color.FromRgb(73, 77, 100),
            [KeyPrimaryText] = Color.FromRgb(202, 211, 245),
            [KeySecondaryText] = Color.FromRgb(165, 173, 203),
            [KeyTertiaryText] = Color.FromRgb(128, 135, 162),
            [KeyAccentColor] = Color.FromRgb(198, 160, 246),
            [KeyAccentForeground] = Color.FromRgb(24, 25, 38),
            [KeyButtonBackground] = Color.FromRgb(54, 58, 79),
            [KeyButtonHover] = Color.FromRgb(73, 77, 100),
            [KeyButtonPressed] = Color.FromRgb(138, 173, 244),
            [KeyControlBackground] = Color.FromRgb(30, 32, 48),
            [KeyControlBorder] = Color.FromRgb(73, 77, 100),
            [KeyInputBackground] = Color.FromRgb(30, 32, 48),
            [KeyTabUnselected] = Color.FromRgb(30, 32, 48),
            [KeyComboBoxBackground] = Color.FromRgb(30, 32, 48),
            [KeyComboBoxItemHover] = Color.FromRgb(54, 58, 79),
            [KeyProgressBarBackground] = Color.FromRgb(54, 58, 79),
            [KeyStatusTextWarning] = Color.FromRgb(238, 212, 159),
            [KeyGroupHeaderBackground] = Color.FromRgb(30, 32, 48),
            [KeyGroupHeaderBorder] = Color.FromRgb(73, 77, 100),
            [KeyCardBackground] = Color.FromRgb(30, 32, 48),
            [KeyCardBorder] = Color.FromRgb(73, 77, 100),
            [KeyScrollBarBackground] = Color.FromRgb(30, 32, 48),
            [KeyScrollBarForeground] = Color.FromRgb(110, 115, 141),
            [KeyScrollBarHover] = Color.FromRgb(147, 154, 183),
            [KeyLinkForeground] = Color.FromRgb(138, 173, 244),
        },
        [AppTheme.CatppuccinLatte] = new(StringComparer.Ordinal)
        {
            [KeyBackground] = Color.FromRgb(239, 241, 245),
            [KeyHeaderBackground] = Color.FromRgb(230, 233, 239),
            [KeyFooterBackground] = Color.FromRgb(230, 233, 239),
            [KeyBorderColor] = Color.FromRgb(188, 192, 204),
            [KeyPrimaryText] = Color.FromRgb(76, 79, 105),
            [KeySecondaryText] = Color.FromRgb(108, 111, 133),
            [KeyTertiaryText] = Color.FromRgb(140, 143, 161),
            [KeyAccentColor] = Color.FromRgb(136, 57, 239),
            [KeyAccentForeground] = Color.FromRgb(255, 255, 255),
            [KeyButtonBackground] = Color.FromRgb(204, 208, 218),
            [KeyButtonHover] = Color.FromRgb(188, 192, 204),
            [KeyButtonPressed] = Color.FromRgb(30, 102, 245),
            [KeyControlBackground] = Color.FromRgb(230, 233, 239),
            [KeyControlBorder] = Color.FromRgb(188, 192, 204),
            [KeyInputBackground] = Color.FromRgb(255, 255, 255),
            [KeyTabUnselected] = Color.FromRgb(220, 224, 232),
            [KeyComboBoxBackground] = Color.FromRgb(255, 255, 255),
            [KeyComboBoxItemHover] = Color.FromRgb(220, 224, 232),
            [KeyProgressBarBackground] = Color.FromRgb(220, 224, 232),
            [KeyStatusTextWarning] = Color.FromRgb(223, 142, 29),
            [KeyGroupHeaderBackground] = Color.FromRgb(230, 233, 239),
            [KeyGroupHeaderBorder] = Color.FromRgb(188, 192, 204),
            [KeyCardBackground] = Color.FromRgb(255, 255, 255),
            [KeyCardBorder] = Color.FromRgb(188, 192, 204),
            [KeyScrollBarBackground] = Color.FromRgb(230, 233, 239),
            [KeyScrollBarForeground] = Color.FromRgb(156, 160, 176),
            [KeyScrollBarHover] = Color.FromRgb(108, 111, 133),
            [KeyLinkForeground] = Color.FromRgb(30, 102, 245),
        },
        [AppTheme.Dark] = new(StringComparer.Ordinal)
        {
            [KeyBackground] = Color.FromRgb(26, 26, 26),
            [KeyHeaderBackground] = Color.FromRgb(36, 36, 36),
            [KeyFooterBackground] = Color.FromRgb(36, 36, 36),
            [KeyBorderColor] = Color.FromRgb(51, 51, 51),
            [KeyPrimaryText] = Color.FromRgb(232, 232, 232),
            [KeySecondaryText] = Color.FromRgb(160, 160, 160),
            [KeyTertiaryText] = Color.FromRgb(102, 102, 102),
            [KeyAccentColor] = Color.FromRgb(59, 130, 246),
            [KeyAccentForeground] = Color.FromRgb(255, 255, 255),
            [KeyButtonBackground] = Color.FromRgb(46, 46, 46),
            [KeyButtonHover] = Color.FromRgb(51, 51, 51),
            [KeyButtonPressed] = Color.FromRgb(59, 130, 246),
            [KeyControlBackground] = Color.FromRgb(36, 36, 36),
            [KeyControlBorder] = Color.FromRgb(51, 51, 51),
            [KeyInputBackground] = Color.FromRgb(36, 36, 36),
            [KeyTabUnselected] = Color.FromRgb(36, 36, 36),
            [KeyComboBoxBackground] = Color.FromRgb(36, 36, 36),
            [KeyComboBoxItemHover] = Color.FromRgb(51, 51, 51),
            [KeyProgressBarBackground] = Color.FromRgb(46, 46, 46),
            [KeyStatusTextWarning] = Color.FromRgb(234, 179, 8),
            [KeyGroupHeaderBackground] = Color.FromRgb(36, 36, 36),
            [KeyGroupHeaderBorder] = Color.FromRgb(51, 51, 51),
            [KeyCardBackground] = Color.FromRgb(46, 46, 46),
            [KeyCardBorder] = Color.FromRgb(51, 51, 51),
            [KeyScrollBarBackground] = Color.FromRgb(36, 36, 36),
            [KeyScrollBarForeground] = Color.FromRgb(102, 102, 102),
            [KeyScrollBarHover] = Color.FromRgb(160, 160, 160),
            [KeyLinkForeground] = Color.FromRgb(96, 165, 250),
        },
    };

    public static void ApplyTheme(AppTheme theme)
    {
        Preferences.Theme = theme;
        var resources = Current?.Resources;
        if (resources == null)
        {
            return;
        }

        if (!ThemePalettes.TryGetValue(theme, out var palette))
        {
            palette = ThemePalettes[AppTheme.Dark];
        }

        foreach (var (key, color) in palette)
        {
            SetBrushColor(resources, key, color);
        }
    }
#pragma warning restore MA0051

    /// <summary>
    /// Returns true if the theme has a palette defined. Used by tests.
    /// </summary>
    /// <returns></returns>
    internal static bool HasThemePalette(AppTheme theme) => ThemePalettes.ContainsKey(theme);

    /// <summary>
    /// Returns any required keys missing from the theme's palette. Used by tests.
    /// </summary>
    /// <returns></returns>
    internal static IReadOnlyList<string> GetMissingThemeKeys(AppTheme theme, string[] requiredKeys)
    {
        if (!ThemePalettes.TryGetValue(theme, out var palette))
        {
            return requiredKeys;
        }

        return requiredKeys.Where(key => !palette.ContainsKey(key)).ToList();
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
