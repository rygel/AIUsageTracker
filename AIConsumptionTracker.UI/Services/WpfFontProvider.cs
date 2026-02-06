using System.Windows.Media;
using AIConsumptionTracker.Core.Interfaces;

namespace AIConsumptionTracker.UI.Services
{
    public class WpfFontProvider : IFontProvider
    {
        public IEnumerable<string> GetInstalledFonts()
        {
            return Fonts.SystemFontFamilies
                .OrderBy(f => f.Source)
                .Select(f => f.Source);
        }
    }
}
