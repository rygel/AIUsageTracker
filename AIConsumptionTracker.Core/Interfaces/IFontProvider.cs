namespace AIConsumptionTracker.Core.Interfaces
{
    public interface IFontProvider
    {
        IEnumerable<string> GetInstalledFonts();
    }
}
