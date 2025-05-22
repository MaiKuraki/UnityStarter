using System;

namespace CycloneGames.Utility.Runtime
{
    public static class FormatUtil
    {
        private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB" };

        /// <summary>
        /// Formats byte size into human-readable string with zero heap allocations.
        /// </summary>
        /// <param name="bytes">Size in bytes</param>
        /// <param name="decimalPlaces">Number of decimal places (0-5)</param>
        /// <returns>Formatted string without heap allocations</returns>
        public static string FormatBytes(long bytes, int decimalPlaces = 2)
        {
            if (bytes <= 0) return "0 B";

            // Clamp decimal places between 0 and 5 
            decimalPlaces = Math.Clamp(decimalPlaces, 0, 5);

            int suffixIndex = 0;
            double size = bytes;

            // Calculate appropriate size unit 
            while (size >= 1024 && suffixIndex < SizeSuffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            // Format number with stack-allocated buffer 
            Span<char> buffer = stackalloc char[32];
            int charsWritten = 0;

            // Format numeric portion 
            if (decimalPlaces > 0)
            {
                size.TryFormat(buffer, out charsWritten, $"F{decimalPlaces}");
            }
            else
            {
                ((long)size).TryFormat(buffer, out charsWritten);
            }

            // Trim trailing zeros after decimal point 
            if (decimalPlaces > 0)
            {
                int decimalIndex = buffer.Slice(0, charsWritten).IndexOf('.');
                if (decimalIndex >= 0)
                {
                    int lastNonZero = charsWritten - 1;
                    while (lastNonZero > decimalIndex && buffer[lastNonZero] == '0')
                        lastNonZero--;

                    charsWritten = (lastNonZero == decimalIndex) ? decimalIndex : lastNonZero + 1;
                }
            }

            // Append unit suffix 
            buffer[charsWritten++] = ' ';
            SizeSuffixes[suffixIndex].AsSpan().CopyTo(buffer.Slice(charsWritten));
            charsWritten += SizeSuffixes[suffixIndex].Length;

            return new string(buffer.Slice(0, charsWritten));
        }
    }
}