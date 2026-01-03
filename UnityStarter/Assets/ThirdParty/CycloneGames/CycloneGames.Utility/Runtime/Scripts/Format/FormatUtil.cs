using System;

namespace CycloneGames.Utility.Runtime
{
    public static class FormatUtil
    {
        private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB" };

        // Pre-cached format strings to avoid interpolation allocation
        private static readonly string[] DecimalFormats = { "F0", "F1", "F2", "F3", "F4", "F5" };

        /// <summary>
        /// Formats byte size into human-readable string.
        /// Note: Returns a new string allocation. Use AppendFormattedBytes for 0GC StringBuilder operations.
        /// </summary>
        public static string FormatBytes(long bytes, int decimalPlaces = 2)
        {
            if (bytes <= 0) return "0 B";

            decimalPlaces = Math.Clamp(decimalPlaces, 0, 5);

            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < SizeSuffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            Span<char> buffer = stackalloc char[32];
            int charsWritten = 0;

            if (decimalPlaces > 0)
            {
                size.TryFormat(buffer, out charsWritten, DecimalFormats[decimalPlaces]);
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

            buffer[charsWritten++] = ' ';
            SizeSuffixes[suffixIndex].AsSpan().CopyTo(buffer.Slice(charsWritten));
            charsWritten += SizeSuffixes[suffixIndex].Length;

            return new string(buffer.Slice(0, charsWritten));
        }

        /// <summary>
        /// Appends formatted byte size to a StringBuilder with zero heap allocations.
        /// </summary>
        public static void AppendFormattedBytes(this System.Text.StringBuilder sb, long bytes, int decimalPlaces = 2)
        {
            if (sb == null) return;
            if (bytes <= 0)
            {
                sb.Append("0 B");
                return;
            }

            decimalPlaces = Math.Clamp(decimalPlaces, 0, 5);
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < SizeSuffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            Span<char> buffer = stackalloc char[32];
            int charsWritten = 0;

            if (decimalPlaces > 0)
            {
                size.TryFormat(buffer, out charsWritten, DecimalFormats[decimalPlaces]);
            }
            else
            {
                ((long)size).TryFormat(buffer, out charsWritten);
            }

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

            for (int i = 0; i < charsWritten; i++)
            {
                sb.Append(buffer[i]);
            }

            sb.Append(' ');
            sb.Append(SizeSuffixes[suffixIndex]);
        }
    }
}