namespace CycloneGames.Utility.Runtime
{
    public static class MemoryFormatExtensions
    {
        public static string ToMemorySizeString(this long bytes, int decimalPlaces = 2)
        {
            return FormatUtil.FormatBytes(bytes, decimalPlaces);
        }

        public static string ToCompactString(this long number, int decimalPlaces = 2)
        {
            return FormatUtil.FormatNumber(number, decimalPlaces);
        }

        public static string ToCompactString(this int number, int decimalPlaces = 2)
        {
            return FormatUtil.FormatNumber(number, decimalPlaces);
        }

        public static string ToDurationString(this float seconds, bool showMilliseconds = false)
        {
            return FormatUtil.FormatDuration(seconds, showMilliseconds);
        }

        public static string ToDurationString(this double seconds, bool showMilliseconds = false)
        {
            return FormatUtil.FormatDuration(seconds, showMilliseconds);
        }

        public static string ToPercentString(this float ratio, int decimalPlaces = 1)
        {
            return FormatUtil.FormatPercent(ratio, decimalPlaces);
        }
    }
}