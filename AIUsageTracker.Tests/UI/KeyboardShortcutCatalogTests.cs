// <copyright file="KeyboardShortcutCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows.Input;

using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

public sealed class KeyboardShortcutCatalogTests
{
    [Theory]
    [InlineData(ModifierKeys.Control, Key.R, 1)]
    [InlineData(ModifierKeys.Control, Key.P, 2)]
    [InlineData(ModifierKeys.Control, Key.Q, 3)]
    [InlineData(ModifierKeys.None, Key.Escape, 3)]
    [InlineData(ModifierKeys.None, Key.F2, 4)]
    [InlineData(ModifierKeys.Control, Key.F2, 0)]
    [InlineData(ModifierKeys.None, Key.R, 0)]
    public void Resolve_MapsExpectedShortcutAction(
        ModifierKeys modifiers,
        Key key,
        int expectedActionValue)
    {
        var action = KeyboardShortcutCatalog.Resolve(modifiers, key);

        Assert.Equal((KeyboardShortcutAction)expectedActionValue, action);
    }
}
