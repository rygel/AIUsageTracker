// <copyright file="KeyboardShortcutCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows.Input;

namespace AIUsageTracker.UI.Slim;

internal static class KeyboardShortcutCatalog
{
    public static KeyboardShortcutAction Resolve(ModifierKeys modifiers, Key key)
    {
        if (modifiers == ModifierKeys.Control)
        {
            return key switch
            {
                Key.R => KeyboardShortcutAction.Refresh,
                Key.P => KeyboardShortcutAction.TogglePrivacy,
                Key.Q => KeyboardShortcutAction.Close,
                _ => KeyboardShortcutAction.None,
            };
        }

        return key switch
        {
            Key.Escape => KeyboardShortcutAction.Close,
            Key.F2 => KeyboardShortcutAction.OpenSettings,
            _ => KeyboardShortcutAction.None,
        };
    }
}
