using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Localization.Core
{
    /// <summary>
    /// Pure-function pseudo-localizer. Zero allocation on the hot path when disabled.
    /// All methods are static and thread-safe with no shared mutable state.
    /// <para>
    /// <b>Usage</b>: Call <see cref="Transform"/> on resolved strings inside
    /// <see cref="LocalizationService"/> when pseudo mode is active.
    /// </para>
    /// </summary>
    public static class PseudoLocalizer
    {
        // Source text remains ASCII-only; the runtime output still uses accented glyphs.
        private const string LowerAccents =
            "\u00E0\u1E03\u00E7\u010F\u00E9\u0192\u011F\u0125\u00ED\u0135\u0137\u013A\u1E3F" +
            "\u00F1\u00F6\u1E57\u01EB\u0155\u015F\u0163\u00FA\u1E7D\u0175\u1E8B\u00FD\u017E";

        private const string UpperAccents =
            "\u00C0\u1E02\u00C7\u010E\u00C9\u0191\u011E\u0124\u00CD\u0134\u0136\u0139\u1E3E" +
            "\u00D1\u00D6\u1E56\u01EA\u0154\u015E\u0162\u00DA\u1E7C\u0174\u1E8A\u00DD\u017D";

        private const string PadChars = "~";

        /// <summary>
        /// Transforms a resolved string according to the active pseudo-locale mode.
        /// Returns the original string reference when <paramref name="mode"/> is <see cref="PseudoLocaleMode.None"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Transform(string input, PseudoLocaleMode mode)
        {
            if (mode == PseudoLocaleMode.None || input == null || input.Length == 0)
                return input;

            return TransformCore(input, mode);
        }

        private static string TransformCore(string input, PseudoLocaleMode mode)
        {
            bool accents = (mode & PseudoLocaleMode.Accents) != 0;
            bool elongate = (mode & PseudoLocaleMode.Elongate) != 0;
            bool brackets = (mode & PseudoLocaleMode.Brackets) != 0;
            bool mirror = (mode & PseudoLocaleMode.Mirror) != 0;

            int extraLen = 0;
            if (brackets) extraLen += 2;
            if (elongate) extraLen += (input.Length + 2) / 3;

            int maxLen = input.Length + extraLen;
            Span<char> buffer = maxLen <= 512
                ? stackalloc char[maxLen]
                : new char[maxLen];

            int pos = 0;

            if (brackets)
                buffer[pos++] = '[';

            if (mirror)
            {
                for (int i = input.Length - 1; i >= 0; i--)
                {
                    char c = input[i];
                    buffer[pos++] = accents ? AccentChar(c) : c;
                }
            }
            else
            {
                for (int i = 0; i < input.Length; i++)
                {
                    char c = input[i];
                    buffer[pos++] = accents ? AccentChar(c) : c;
                }
            }

            if (elongate)
            {
                int padCount = Math.Max(1, (input.Length + 2) / 3);
                for (int i = 0; i < padCount; i++)
                    buffer[pos++] = PadChars[0];
            }

            if (brackets)
                buffer[pos++] = ']';

            return new string(buffer.Slice(0, pos));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static char AccentChar(char c)
        {
            if (c >= 'a' && c <= 'z') return LowerAccents[c - 'a'];
            if (c >= 'A' && c <= 'Z') return UpperAccents[c - 'A'];
            return c;
        }
    }
}
