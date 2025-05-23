using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace CycloneGames.Logger.Util
{
    /// <summary>
    /// Provides utility methods for formatting DateTime objects with low GC impact.
    /// </summary>
    public static class DateTimeUtil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AppendTwoDigits(StringBuilder sb, int value)
        {
            sb.Append((char)('0' + value / 10));
            sb.Append((char)('0' + value % 10));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AppendThreeDigits(StringBuilder sb, int value)
        {
            sb.Append((char)('0' + value / 100));
            sb.Append((char)('0' + (value / 10) % 10));
            sb.Append((char)('0' + value % 10));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AppendFourDigits(StringBuilder sb, int value)
        {
            sb.Append((char)('0' + value / 1000));
            sb.Append((char)('0' + (value / 100) % 10));
            sb.Append((char)('0' + (value / 10) % 10));
            sb.Append((char)('0' + value % 10));
        }

        /// <summary>
        /// Formats DateTime to "yyyy-MM-dd HH:mm:ss.fff" into the provided StringBuilder.
        /// This method aims for minimal GC allocation during formatting.
        /// </summary>
        public static void FormatDateTimePrecise(DateTime dt, StringBuilder sb)
        {
            AppendFourDigits(sb, dt.Year);
            sb.Append('-');
            AppendTwoDigits(sb, dt.Month);
            sb.Append('-');
            AppendTwoDigits(sb, dt.Day);
            sb.Append(' ');
            AppendTwoDigits(sb, dt.Hour);
            sb.Append(':');
            AppendTwoDigits(sb, dt.Minute);
            sb.Append(':');
            AppendTwoDigits(sb, dt.Second);
            sb.Append('.');
            AppendThreeDigits(sb, dt.Millisecond);
        }
    }
}