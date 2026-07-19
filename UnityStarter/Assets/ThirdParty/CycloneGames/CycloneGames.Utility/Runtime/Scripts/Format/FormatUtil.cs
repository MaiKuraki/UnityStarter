using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace CycloneGames.Utility.Runtime
{
    public static class FormatUtil
    {
        private const int MaxDecimalPlaces = 5;
        private const double MaxDurationSecondsExclusive = 9223372036854775808d;

        // These labels intentionally preserve the existing public output contract (base-1024 values
        // with legacy KB/MB labels). A future unit-label correction requires an explicit migration.
        private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB" };
        private static readonly string[] NumberSuffixes = { "", "K", "M", "B", "T" };
        private static readonly string[] DecimalFormats = { "F0", "F1", "F2", "F3", "F4", "F5" };

        /// <summary>
        /// Formats a non-negative byte count using invariant culture. The returned string is a new allocation.
        /// </summary>
        public static string FormatBytes(long bytes, int decimalPlaces = 2)
        {
            ValidateNonNegative(bytes, nameof(bytes));
            ValidateDecimalPlaces(decimalPlaces);

            Span<char> buffer = stackalloc char[64];
            if (!TryFormatBytes(bytes, buffer, out int charsWritten, decimalPlaces))
            {
                throw new InvalidOperationException("The internal byte-format buffer is too small.");
            }
            return new string(buffer.Slice(0, charsWritten));
        }

        /// <summary>
        /// Attempts to format a non-negative byte count into <paramref name="destination"/> using invariant culture.
        /// Returns false for invalid arguments or insufficient destination capacity.
        /// </summary>
        public static bool TryFormatBytes(
            long bytes,
            Span<char> destination,
            out int charsWritten,
            int decimalPlaces = 2)
        {
            charsWritten = 0;
            if (bytes < 0 || !IsValidDecimalPlaces(decimalPlaces))
            {
                return false;
            }

            if (bytes == 0)
            {
                return TryCopy("0 B".AsSpan(), destination, out charsWritten);
            }

            int suffixIndex = 0;
            double size = bytes;
            while (size >= 1024d && suffixIndex < SizeSuffixes.Length - 1)
            {
                size /= 1024d;
                suffixIndex++;
            }

            PromoteRoundedValue(ref size, ref suffixIndex, 1024d, SizeSuffixes.Length, decimalPlaces);

            int position = 0;
            bool formatted = suffixIndex == 0
                ? TryAppendInt64(bytes, destination, ref position)
                : TryAppendFixed(size, decimalPlaces, destination, ref position, true);
            if (!formatted ||
                !TryAppendChar(' ', destination, ref position) ||
                !TryAppend(SizeSuffixes[suffixIndex].AsSpan(), destination, ref position))
            {
                return false;
            }

            charsWritten = position;
            return true;
        }

        /// <summary>
        /// Appends a formatted byte count. The append itself can grow the StringBuilder and allocate.
        /// Pre-size the builder when allocation behavior matters.
        /// </summary>
        public static void AppendFormattedBytes(this StringBuilder sb, long bytes, int decimalPlaces = 2)
        {
            if (sb == null)
            {
                return;
            }
            ValidateNonNegative(bytes, nameof(bytes));
            ValidateDecimalPlaces(decimalPlaces);

            Span<char> buffer = stackalloc char[64];
            if (!TryFormatBytes(bytes, buffer, out int charsWritten, decimalPlaces))
            {
                throw new InvalidOperationException("The internal byte-format buffer is too small.");
            }
            sb.Append(buffer.Slice(0, charsWritten));
        }

        /// <summary>
        /// Formats a number into a compact invariant representation such as "1.23M".
        /// </summary>
        public static string FormatNumber(long number, int decimalPlaces = 2)
        {
            ValidateDecimalPlaces(decimalPlaces);

            Span<char> buffer = stackalloc char[64];
            if (!TryFormatNumber(number, buffer, out int charsWritten, decimalPlaces))
            {
                throw new InvalidOperationException("The internal number-format buffer is too small.");
            }
            return new string(buffer.Slice(0, charsWritten));
        }

        /// <summary>
        /// Attempts to format a compact number into <paramref name="destination"/> using invariant culture.
        /// Returns false for an invalid decimal-place count or insufficient destination capacity.
        /// </summary>
        public static bool TryFormatNumber(
            long number,
            Span<char> destination,
            out int charsWritten,
            int decimalPlaces = 2)
        {
            charsWritten = 0;
            if (!IsValidDecimalPlaces(decimalPlaces))
            {
                return false;
            }

            if (number == 0)
            {
                return TryCopy("0".AsSpan(), destination, out charsWritten);
            }

            bool negative = number < 0;
            double magnitude = negative ? -(double)number : number;
            int suffixIndex = 0;
            while (magnitude >= 1000d && suffixIndex < NumberSuffixes.Length - 1)
            {
                magnitude /= 1000d;
                suffixIndex++;
            }

            PromoteRoundedValue(ref magnitude, ref suffixIndex, 1000d, NumberSuffixes.Length, decimalPlaces);

            int position = 0;
            if (suffixIndex == 0)
            {
                if (!TryAppendInt64(number, destination, ref position))
                {
                    return false;
                }
            }
            else
            {
                if (negative && !TryAppendChar('-', destination, ref position))
                {
                    return false;
                }
                if (!TryAppendFixed(magnitude, decimalPlaces, destination, ref position, true) ||
                    !TryAppend(NumberSuffixes[suffixIndex].AsSpan(), destination, ref position))
                {
                    return false;
                }
            }

            charsWritten = position;
            return true;
        }

        /// <summary>
        /// Appends a compact invariant number. The StringBuilder can allocate if its capacity must grow.
        /// </summary>
        public static void AppendFormattedNumber(this StringBuilder sb, long number, int decimalPlaces = 2)
        {
            if (sb == null)
            {
                return;
            }
            ValidateDecimalPlaces(decimalPlaces);

            Span<char> buffer = stackalloc char[64];
            if (!TryFormatNumber(number, buffer, out int charsWritten, decimalPlaces))
            {
                throw new InvalidOperationException("The internal number-format buffer is too small.");
            }
            sb.Append(buffer.Slice(0, charsWritten));
        }

        /// <summary>
        /// Formats finite seconds as an invariant duration. Negative values are clamped to zero for compatibility.
        /// </summary>
        public static string FormatDuration(double totalSeconds, bool showMilliseconds = false)
        {
            ValidateDuration(totalSeconds);

            Span<char> buffer = stackalloc char[64];
            if (!TryFormatDuration(totalSeconds, buffer, out int charsWritten, showMilliseconds))
            {
                throw new InvalidOperationException("The internal duration-format buffer is too small.");
            }
            return new string(buffer.Slice(0, charsWritten));
        }

        /// <summary>
        /// Attempts to format finite seconds into <paramref name="destination"/> using invariant culture.
        /// Negative values are clamped to zero. Values outside the Int64-second range return false.
        /// </summary>
        public static bool TryFormatDuration(
            double totalSeconds,
            Span<char> destination,
            out int charsWritten,
            bool showMilliseconds = false)
        {
            charsWritten = 0;
            if (!IsFinite(totalSeconds) || totalSeconds >= MaxDurationSecondsExclusive)
            {
                return false;
            }

            if (totalSeconds < 0d)
            {
                totalSeconds = 0d;
            }

            int position = 0;
            if (totalSeconds < 1d)
            {
                if (!TryAppendFixed(totalSeconds, 2, destination, ref position, false) ||
                    !TryAppendChar('s', destination, ref position))
                {
                    return false;
                }
                charsWritten = position;
                return true;
            }

            long totalWholeSeconds = (long)Math.Floor(totalSeconds);
            long hours = totalWholeSeconds / 3600L;
            int minutes = (int)((totalWholeSeconds % 3600L) / 60L);
            int seconds = (int)(totalWholeSeconds % 60L);

            if (hours > 0)
            {
                if (!TryAppendInt64(hours, destination, ref position) ||
                    !TryAppendChar(':', destination, ref position) ||
                    !TryAppendTwoDigits(minutes, destination, ref position) ||
                    !TryAppendChar(':', destination, ref position) ||
                    !TryAppendTwoDigits(seconds, destination, ref position))
                {
                    return false;
                }
            }
            else
            {
                if (!TryAppendInt64(minutes, destination, ref position) ||
                    !TryAppendChar(':', destination, ref position) ||
                    !TryAppendTwoDigits(seconds, destination, ref position))
                {
                    return false;
                }
            }

            if (showMilliseconds)
            {
                int milliseconds = (int)((totalSeconds - totalWholeSeconds) * 1000d);
                milliseconds = Math.Min(Math.Max(milliseconds, 0), 999);
                if (!TryAppendChar('.', destination, ref position) ||
                    !TryAppendThreeDigits(milliseconds, destination, ref position))
                {
                    return false;
                }
            }

            charsWritten = position;
            return true;
        }

        /// <summary>
        /// Appends an invariant duration. The StringBuilder can allocate if its capacity must grow.
        /// </summary>
        public static void AppendFormattedDuration(this StringBuilder sb, double totalSeconds, bool showMilliseconds = false)
        {
            if (sb == null)
            {
                return;
            }
            ValidateDuration(totalSeconds);

            Span<char> buffer = stackalloc char[64];
            if (!TryFormatDuration(totalSeconds, buffer, out int charsWritten, showMilliseconds))
            {
                throw new InvalidOperationException("The internal duration-format buffer is too small.");
            }
            sb.Append(buffer.Slice(0, charsWritten));
        }

        /// <summary>
        /// Formats a finite ratio in the inclusive range [0, 1] as an invariant percentage.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string FormatPercent(float ratio, int decimalPlaces = 1)
        {
            ValidateRatio(ratio);
            ValidateDecimalPlaces(decimalPlaces);

            Span<char> buffer = stackalloc char[32];
            if (!TryFormatPercent(ratio, buffer, out int charsWritten, decimalPlaces))
            {
                throw new InvalidOperationException("The internal percentage-format buffer is too small.");
            }
            return new string(buffer.Slice(0, charsWritten));
        }

        /// <summary>
        /// Attempts to format a finite ratio in [0, 1] into <paramref name="destination"/>.
        /// Returns false for invalid arguments or insufficient destination capacity.
        /// </summary>
        public static bool TryFormatPercent(
            float ratio,
            Span<char> destination,
            out int charsWritten,
            int decimalPlaces = 1)
        {
            charsWritten = 0;
            if (!IsFinite(ratio) || ratio < 0f || ratio > 1f || !IsValidDecimalPlaces(decimalPlaces))
            {
                return false;
            }

            int position = 0;
            if (!TryAppendFixed(ratio * 100d, decimalPlaces, destination, ref position, true) ||
                !TryAppendChar('%', destination, ref position))
            {
                return false;
            }

            charsWritten = position;
            return true;
        }

        private static bool TryAppendFixed(
            double value,
            int decimalPlaces,
            Span<char> destination,
            ref int position,
            bool trimFraction)
        {
            int start = position;
            if (!value.TryFormat(
                    destination.Slice(position),
                    out int written,
                    DecimalFormats[decimalPlaces].AsSpan(),
                    CultureInfo.InvariantCulture))
            {
                return false;
            }

            position += written;
            if (trimFraction && decimalPlaces > 0)
            {
                TrimTrailingFractionZeros(destination, start, ref position);
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryAppendInt64(long value, Span<char> destination, ref int position)
        {
            if (!value.TryFormat(
                    destination.Slice(position),
                    out int written,
                    default,
                    CultureInfo.InvariantCulture))
            {
                return false;
            }
            position += written;
            return true;
        }

        private static void TrimTrailingFractionZeros(Span<char> destination, int start, ref int end)
        {
            int relativeDecimalIndex = destination.Slice(start, end - start).IndexOf('.');
            if (relativeDecimalIndex < 0)
            {
                return;
            }

            int decimalIndex = start + relativeDecimalIndex;
            int last = end - 1;
            while (last > decimalIndex && destination[last] == '0')
            {
                last--;
            }
            end = last == decimalIndex ? decimalIndex : last + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryAppendChar(char value, Span<char> destination, ref int position)
        {
            if ((uint)position >= (uint)destination.Length)
            {
                return false;
            }
            destination[position++] = value;
            return true;
        }

        private static bool TryAppend(ReadOnlySpan<char> value, Span<char> destination, ref int position)
        {
            if (value.Length > destination.Length - position)
            {
                return false;
            }
            value.CopyTo(destination.Slice(position));
            position += value.Length;
            return true;
        }

        private static bool TryAppendTwoDigits(int value, Span<char> destination, ref int position)
        {
            if (destination.Length - position < 2)
            {
                return false;
            }
            destination[position++] = (char)('0' + value / 10);
            destination[position++] = (char)('0' + value % 10);
            return true;
        }

        private static bool TryAppendThreeDigits(int value, Span<char> destination, ref int position)
        {
            if (destination.Length - position < 3)
            {
                return false;
            }
            destination[position++] = (char)('0' + value / 100);
            destination[position++] = (char)('0' + value / 10 % 10);
            destination[position++] = (char)('0' + value % 10);
            return true;
        }

        private static bool TryCopy(ReadOnlySpan<char> value, Span<char> destination, out int charsWritten)
        {
            if (value.Length > destination.Length)
            {
                charsWritten = 0;
                return false;
            }
            value.CopyTo(destination);
            charsWritten = value.Length;
            return true;
        }

        private static void PromoteRoundedValue(
            ref double value,
            ref int suffixIndex,
            double divisor,
            int suffixCount,
            int decimalPlaces)
        {
            if (suffixIndex >= suffixCount - 1)
            {
                return;
            }

            double rounded = Math.Round(value, decimalPlaces, MidpointRounding.AwayFromZero);
            if (rounded >= divisor)
            {
                value /= divisor;
                suffixIndex++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidDecimalPlaces(int decimalPlaces)
        {
            return (uint)decimalPlaces <= MaxDecimalPlaces;
        }

        private static void ValidateDecimalPlaces(int decimalPlaces)
        {
            if (!IsValidDecimalPlaces(decimalPlaces))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(decimalPlaces),
                    decimalPlaces,
                    $"Decimal places must be between 0 and {MaxDecimalPlaces}.");
            }
        }

        private static void ValidateNonNegative(long value, string parameterName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Value must be non-negative.");
            }
        }

        private static void ValidateDuration(double totalSeconds)
        {
            if (!IsFinite(totalSeconds) || totalSeconds >= MaxDurationSecondsExclusive)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalSeconds),
                    totalSeconds,
                    "Duration must be finite and fit in an Int64 number of whole seconds.");
            }
        }

        private static void ValidateRatio(float ratio)
        {
            if (!IsFinite(ratio) || ratio < 0f || ratio > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(ratio), ratio, "Ratio must be finite and between 0 and 1.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
