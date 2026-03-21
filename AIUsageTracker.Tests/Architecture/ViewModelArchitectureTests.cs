// <copyright file="ViewModelArchitectureTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using AIUsageTracker.UI.Slim.ViewModels;

namespace AIUsageTracker.Tests.Architecture;

/// <summary>
/// Tests that enforce architectural guardrails for ViewModels.
/// </summary>
public class ViewModelArchitectureTests
{
    [Fact]
    public void ViewModels_ShouldNotReference_SystemWindowsNamespaceInFields()
    {
        var viewModelTypes = typeof(MainViewModel).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("ViewModel", StringComparison.Ordinal) && !t.IsAbstract && !t.IsInterface)
            .ToList();

        var violations = new List<string>();

        foreach (var type in viewModelTypes)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            foreach (var field in fields)
            {
                var fieldType = field.FieldType;

                // Skip primitive types and common framework types
                if (fieldType.IsPrimitive || fieldType == typeof(string) || fieldType.Namespace == null)
                {
                    continue;
                }

                // Check if the field type is from System.Windows (WPF UI namespace)
                if (fieldType.Namespace.StartsWith("System.Windows", StringComparison.Ordinal) &&
                    !fieldType.Namespace.StartsWith("System.Windows.Input", StringComparison.Ordinal))
                {
                    violations.Add($"{type.Name}.{field.Name} has UI type {fieldType.Name} from {fieldType.Namespace}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"ViewModels should not reference System.Windows types (except Input).{Environment.NewLine}" +
            $"This ensures ViewModels remain testable without WPF dependencies.{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ViewModels_ShouldInherit_FromBaseViewModel()
    {
        var viewModelTypes = typeof(MainViewModel).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("ViewModel", StringComparison.Ordinal) &&
                        !t.IsAbstract &&
                        !t.IsInterface &&
                        t != typeof(BaseViewModel))
            .ToList();

        var violations = new List<string>();

        foreach (var type in viewModelTypes)
        {
            if (!typeof(BaseViewModel).IsAssignableFrom(type))
            {
                violations.Add($"{type.Name} does not inherit from BaseViewModel");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"All ViewModels should inherit from BaseViewModel for consistent MVVM patterns.{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

}
