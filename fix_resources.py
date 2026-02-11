import re

with open("AIConsumptionTracker.UI/MainWindow.xaml.cs", "r") as f:
    content = f.read()

# Add helper method after constructor
helper_method = """
        private SolidColorBrush GetThemeBrush(string resourceKey, Brush fallback)
        {
            var brush = Application.Current?.Resources[resourceKey] as SolidColorBrush;
            return brush ?? (fallback as SolidColorBrush) ?? Brushes.Gray;
        }

"""

# Find the constructor end and add helper
pattern = r"(            _updateCheckTimer\.Start\(\);\n        \})"
replacement = r"\1" + helper_method
content = re.sub(pattern, replacement, content)


# Replace all (SolidColorBrush)Application.Current.Resources["key"] with GetThemeBrush("key", fallback)
# This pattern matches: (SolidColorBrush)Application.Current.Resources["KEY"]
def replace_resource(match):
    full_match = match.group(0)
    # Extract the key
    key_match = re.search(r'\["([^"]+)"\]', full_match)
    if key_match:
        key = key_match.group(1)
        return f'GetThemeBrush("{key}", Brushes.Gray)'
    return full_match


pattern = r'\(SolidColorBrush\)Application\.Current\.Resources\["[^"]+"\]'
content = re.sub(pattern, replace_resource, content)

with open("AIConsumptionTracker.UI/MainWindow.xaml.cs", "w") as f:
    f.write(content)

print("Done")
