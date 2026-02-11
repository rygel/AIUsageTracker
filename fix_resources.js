const fs = require('fs');

let content = fs.readFileSync('AIConsumptionTracker.UI/MainWindow.xaml.cs', 'utf8');

// Helper method to add
const helperMethod = `
        private SolidColorBrush GetThemeBrush(string resourceKey, Brush fallback)
        {
            var brush = Application.Current?.Resources[resourceKey] as SolidColorBrush;
            return brush ?? (fallback as SolidColorBrush) ?? Brushes.Gray;
        }

`;

// Find constructor end and add helper
const constructorEnd = '            _updateCheckTimer.Start();\n        }';
content = content.replace(constructorEnd, constructorEnd + helperMethod);

// Replace all (SolidColorBrush)Application.Current.Resources["KEY"] with GetThemeBrush("KEY", Brushes.Gray)
content = content.replace(/\(SolidColorBrush\)Application\.Current\.Resources\["([^"]+)"\]/g, 'GetThemeBrush("$1", Brushes.Gray)');

fs.writeFileSync('AIConsumptionTracker.UI/MainWindow.xaml.cs', content);
console.log('Done');