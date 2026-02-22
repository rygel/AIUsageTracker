using AIUsageTracker.Infrastructure.Helpers;
using Xunit;

namespace AIUsageTracker.Tests.Helpers
{
    public class FontSelectionHelperTests
    {
        [Fact]
        public void GetSelectedFont_ReturnsPreference_WhenItExistsInList()
        {
            var available = new[] { "Arial", "Consolas", "Segoe UI" };
            var preference = "Consolas";

            var result = FontSelectionHelper.GetSelectedFont(preference, available);

            Assert.Equal("Consolas", result);
        }

        [Fact]
        public void GetSelectedFont_ReturnsPreference_WhenItExistsInList_CaseInsensitive()
        {
            var available = new[] { "Arial", "Consolas", "Segoe UI" };
            var preference = "consolas";

            var result = FontSelectionHelper.GetSelectedFont(preference, available);

            Assert.Equal("Consolas", result); // Should return the one from the list (correct casing)
        }

        [Fact]
        public void GetSelectedFont_ReturnsPreference_WhenItDoesNotExistInList()
        {
            // User requested: "if in the settings dialog a different font is selected, that this font is supplied"
            // If the user manually set a font (e.g. via config file) that isn't in the list, 
            // we should probably preserve it or fallback. 
            // The Helper implementation returns the preference if not found.
            
            var available = new[] { "Arial", "Segoe UI" };
            var preference = "MyCustomFont";

            var result = FontSelectionHelper.GetSelectedFont(preference, available);

            Assert.Equal("MyCustomFont", result);
        }

        [Fact]
        public void GetSelectedFont_ReturnsSegoeUI_WhenPreferenceEmpty()
        {
            var available = new[] { "Arial", "Segoe UI", "Tahoma" };
            
            var result = FontSelectionHelper.GetSelectedFont(null, available);
            Assert.Equal("Segoe UI", result);

            result = FontSelectionHelper.GetSelectedFont("", available);
            Assert.Equal("Segoe UI", result);
        }

        [Fact]
        public void GetSelectedFont_ReturnsFirst_WhenPreferenceEmptyAndSegoeUIMissing()
        {
            var available = new[] { "Arial", "Tahoma" };
            
            var result = FontSelectionHelper.GetSelectedFont("", available);
            Assert.Equal("Arial", result);
        }
    }
}

