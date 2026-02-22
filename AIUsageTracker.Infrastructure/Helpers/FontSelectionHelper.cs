namespace AIUsageTracker.Infrastructure.Helpers
{
    public static class FontSelectionHelper
    {
        public static string GetSelectedFont(string? currentPreference, IEnumerable<string> availableFonts)
        {
            if (string.IsNullOrWhiteSpace(currentPreference))
            {
                // Fallback: Segoe UI or first available
                return availableFonts.FirstOrDefault(f => f.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase)) 
                       ?? availableFonts.FirstOrDefault() 
                       ?? string.Empty;
            }

            // Try to find exact match
            var match = availableFonts.FirstOrDefault(f => f.Equals(currentPreference, StringComparison.OrdinalIgnoreCase));
            
            // If match found, return it.
            // If NOT found, we should still return the preference? 
            // The user might have a font installed that is temporarily missing or it's a valid font name.
            // BUT, the goal is to select it in a specific list.
            // If we return a value NOT in the list, a ComboBox with IsEditable=false might show nothing or behave weirdly
            // unless we add it to the list. 
            // For now, let's return the match if found. 
            // If not found, returning the preference allows the UI (if editable) to show it, or we might want to fallback.
            // Given "make all fonts available", if it's not in the system list, it's likely invalid.
            // However, usually it's safer to return the preference so we don't destructively overwrite settings 
            // just because a font failed to load.
            // BUT, for the ComboBox selection to work in ReadOnly mode, it usually needs to be in the ItemsSource.
            
            // Let's stick to the logic: return match if found, else return preference (allowing UI to handle it).
            return match ?? currentPreference;
        }
    }
}

