namespace CycloneGames.Utility.Runtime
{
    public static class MemoryFormatExtensions
    {
        public static string ToMemorySizeString(this long bytes, int decimalPlaces = 2)
        {
            return FormatUtil.FormatBytes(bytes, decimalPlaces);
        }
    }
}