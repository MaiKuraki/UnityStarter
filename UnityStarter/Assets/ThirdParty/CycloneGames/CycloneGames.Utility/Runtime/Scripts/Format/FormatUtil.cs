using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Utility.Runtime
{
    public static class FormatUtil
    {
        private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB" };
        private static readonly string[] NumberSuffixes = { "", "K", "M", "B", "T" };

        // Pre-cached format strings to avoid interpolation allocation
        private static readonly string[] DecimalFormats = { "F0", "F1", "F2", "F3", "F4", "F5" };

        /// <summary>
        /// Formats byte size into human-readable string (e.g. "1.5 MB").
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

            sb.Append(buffer.Slice(0, charsWritten));
            sb.Append(' ');
            sb.Append(SizeSuffixes[suffixIndex]);
        }

        // --- Number Formatting ---

        /// <summary>
        /// Formats a large number into a compact human-readable string (e.g. 1234567 → "1.23M").
        /// Useful for scores, currency, damage numbers, etc.
        /// </summary>
        public static string FormatNumber(long number, int decimalPlaces = 2)
        {
            if (number == 0) return "0";

            bool negative = number < 0;
            double abs = negative ? -(double)number : number;

            decimalPlaces = Math.Clamp(decimalPlaces, 0, 5);

            int suffixIndex = 0;
            while (abs >= 1000 && suffixIndex < NumberSuffixes.Length - 1)
            {
                abs /= 1000;
                suffixIndex++;
            }

            Span<char> buffer = stackalloc char[32];
            int pos = 0;

            if (negative)
            {
                buffer[pos++] = '-';
            }

            if (suffixIndex == 0)
            {
                // No suffix — write the absolute value directly (sign already handled)
                ((long)abs).TryFormat(buffer.Slice(pos), out int written);
                pos += written;
            }
            else
            {
                if (decimalPlaces > 0)
                {
                    abs.TryFormat(buffer.Slice(pos), out int written, DecimalFormats[decimalPlaces]);
                    pos += written;

                    // Trim trailing zeros
                    int decimalIdx = buffer.Slice(0, pos).IndexOf('.');
                    if (decimalIdx >= 0)
                    {
                        int lastNonZero = pos - 1;
                        while (lastNonZero > decimalIdx && buffer[lastNonZero] == '0')
                            lastNonZero--;
                        pos = (lastNonZero == decimalIdx) ? decimalIdx : lastNonZero + 1;
                    }
                }
                else
                {
                    ((long)abs).TryFormat(buffer.Slice(pos), out int written);
                    pos += written;
                }

                // Append suffix
                string suffix = NumberSuffixes[suffixIndex];
                suffix.AsSpan().CopyTo(buffer.Slice(pos));
                pos += suffix.Length;
            }

            return new string(buffer.Slice(0, pos));
        }

        /// <summary>
        /// Appends a compact number to a StringBuilder with zero heap allocations.
        /// </summary>
        public static void AppendFormattedNumber(this System.Text.StringBuilder sb, long number, int decimalPlaces = 2)
        {
            if (sb == null) return;

            Span<char> buffer = stackalloc char[32];
            // Reuse the formatting logic via a local span write
            bool negative = number < 0;
            double abs = negative ? -(double)number : number;
            decimalPlaces = Math.Clamp(decimalPlaces, 0, 5);

            int suffixIndex = 0;
            while (abs >= 1000 && suffixIndex < NumberSuffixes.Length - 1)
            {
                abs /= 1000;
                suffixIndex++;
            }

            int pos = 0;
            if (negative) buffer[pos++] = '-';

            if (suffixIndex == 0)
            {
                ((long)abs).TryFormat(buffer.Slice(pos), out int written);
                pos += written;
            }
            else
            {
                if (decimalPlaces > 0)
                {
                    abs.TryFormat(buffer.Slice(pos), out int written, DecimalFormats[decimalPlaces]);
                    pos += written;
                    int decimalIdx = buffer.Slice(0, pos).IndexOf('.');
                    if (decimalIdx >= 0)
                    {
                        int lastNonZero = pos - 1;
                        while (lastNonZero > decimalIdx && buffer[lastNonZero] == '0') lastNonZero--;
                        pos = (lastNonZero == decimalIdx) ? decimalIdx : lastNonZero + 1;
                    }
                }
                else
                {
                    ((long)abs).TryFormat(buffer.Slice(pos), out int written);
                    pos += written;
                }

                string suffix = NumberSuffixes[suffixIndex];
                suffix.AsSpan().CopyTo(buffer.Slice(pos));
                pos += suffix.Length;
            }

            sb.Append(buffer.Slice(0, pos));
        }

        // --- Time Formatting ---

        /// <summary>
        /// Formats seconds into a human-readable duration string.
        /// Examples: 65.3 → "1:05", 3661.5 → "1:01:01", 0.5 → "0.50s"
        /// </summary>
        public static string FormatDuration(double totalSeconds, bool showMilliseconds = false)
        {
            if (totalSeconds < 0) totalSeconds = 0;

            Span<char> buffer = stackalloc char[32];
            int pos = 0;

            if (totalSeconds < 1.0)
            {
                // Sub-second: show as "0.XXs"
                totalSeconds.TryFormat(buffer, out pos, "F2");
                buffer[pos++] = 's';
                return new string(buffer.Slice(0, pos));
            }

            int totalSec = (int)totalSeconds;
            int hours = totalSec / 3600;
            int minutes = (totalSec % 3600) / 60;
            int seconds = totalSec % 60;

            if (hours > 0)
            {
                hours.TryFormat(buffer.Slice(pos), out int w);
                pos += w;
                buffer[pos++] = ':';
                buffer[pos++] = (char)('0' + minutes / 10);
                buffer[pos++] = (char)('0' + minutes % 10);
                buffer[pos++] = ':';
                buffer[pos++] = (char)('0' + seconds / 10);
                buffer[pos++] = (char)('0' + seconds % 10);
            }
            else
            {
                minutes.TryFormat(buffer.Slice(pos), out int w);
                pos += w;
                buffer[pos++] = ':';
                buffer[pos++] = (char)('0' + seconds / 10);
                buffer[pos++] = (char)('0' + seconds % 10);
            }

            if (showMilliseconds)
            {
                int ms = (int)((totalSeconds - totalSec) * 1000);
                buffer[pos++] = '.';
                buffer[pos++] = (char)('0' + ms / 100);
                buffer[pos++] = (char)('0' + (ms / 10) % 10);
                buffer[pos++] = (char)('0' + ms % 10);
            }

            return new string(buffer.Slice(0, pos));
        }

        /// <summary>
        /// Appends a formatted duration to a StringBuilder. Zero heap allocations.
        /// </summary>
        public static void AppendFormattedDuration(this System.Text.StringBuilder sb, double totalSeconds, bool showMilliseconds = false)
        {
            if (sb == null) return;
            if (totalSeconds < 0) totalSeconds = 0;

            Span<char> buffer = stackalloc char[32];
            int pos = 0;

            if (totalSeconds < 1.0)
            {
                totalSeconds.TryFormat(buffer, out pos, "F2");
                buffer[pos++] = 's';
                sb.Append(buffer.Slice(0, pos));
                return;
            }

            int totalSec = (int)totalSeconds;
            int hours = totalSec / 3600;
            int minutes = (totalSec % 3600) / 60;
            int seconds = totalSec % 60;

            if (hours > 0)
            {
                hours.TryFormat(buffer.Slice(pos), out int w);
                pos += w;
                buffer[pos++] = ':';
                buffer[pos++] = (char)('0' + minutes / 10);
                buffer[pos++] = (char)('0' + minutes % 10);
                buffer[pos++] = ':';
                buffer[pos++] = (char)('0' + seconds / 10);
                buffer[pos++] = (char)('0' + seconds % 10);
            }
            else
            {
                minutes.TryFormat(buffer.Slice(pos), out int w);
                pos += w;
                buffer[pos++] = ':';
                buffer[pos++] = (char)('0' + seconds / 10);
                buffer[pos++] = (char)('0' + seconds % 10);
            }

            if (showMilliseconds)
            {
                int ms = (int)((totalSeconds - totalSec) * 1000);
                buffer[pos++] = '.';
                buffer[pos++] = (char)('0' + ms / 100);
                buffer[pos++] = (char)('0' + (ms / 10) % 10);
                buffer[pos++] = (char)('0' + ms % 10);
            }

            sb.Append(buffer.Slice(0, pos));
        }

        // --- Percentage Formatting ---

        /// <summary>
        /// Formats a 0-1 float as a percentage string (e.g. 0.753 → "75.3%").
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string FormatPercent(float ratio, int decimalPlaces = 1)
        {
            decimalPlaces = Math.Clamp(decimalPlaces, 0, 5);
            double pct = ratio * 100.0;
            Span<char> buffer = stackalloc char[16];
            int pos;
            pct.TryFormat(buffer, out pos, DecimalFormats[decimalPlaces]);

            // Trim trailing zeros
            if (decimalPlaces > 0)
            {
                int decimalIdx = buffer.Slice(0, pos).IndexOf('.');
                if (decimalIdx >= 0)
                {
                    int lastNonZero = pos - 1;
                    while (lastNonZero > decimalIdx && buffer[lastNonZero] == '0') lastNonZero--;
                    pos = (lastNonZero == decimalIdx) ? decimalIdx : lastNonZero + 1;
                }
            }

            buffer[pos++] = '%';
            return new string(buffer.Slice(0, pos));
        }
    }
}