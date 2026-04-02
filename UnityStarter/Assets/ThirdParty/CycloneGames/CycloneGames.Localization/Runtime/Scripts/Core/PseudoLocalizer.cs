using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Pseudo-localization modes for QA testing without real translations.
    /// Multiple modes can be combined via bitwise OR.
    /// </summary>
    [Flags]
    public enum PseudoLocaleMode : byte
    {
        /// <summary>Disabled — pass-through original text.</summary>
        None = 0,

        /// <summary>Replace ASCII letters with accented variants (a→à, e→é, etc.).</summary>
        Accents = 1 << 0,

        /// <summary>Pad text with ~33% extra characters to simulate longer translations (German, Finnish).</summary>
        Elongate = 1 << 1,

        /// <summary>Wrap text in brackets [「text」] to detect truncation and hardcoded strings.</summary>
        Brackets = 1 << 2,

        /// <summary>Mirror text for RTL testing (reverses character order).</summary>
        Mirror = 1 << 3,

        /// <summary>Accents + Elongate + Brackets — the most common QA preset.</summary>
        Full = Accents | Elongate | Brackets,
    }

    /// <summary>
    /// Pure-function pseudo-localizer. Zero allocation on the hot path when disabled.
    /// All methods are static and thread-safe — no shared mutable state.
    /// <para>
    /// <b>Usage</b>: Call <see cref="Transform"/> on resolved strings inside
    /// <see cref="LocalizationService"/> when pseudo mode is active.
    /// </para>
    /// </summary>
    public static class PseudoLocalizer
    {
        // ── Accent map (ASCII a-z / A-Z → accented Unicode) ─────
        // Chose visually similar glyphs so text remains readable during QA.
        // Length = 26, indexed by (char - 'a') or (char - 'A').

        private const string LowerAccents = "àƀçďéƒĝĥíĵķĺɱñöƥɋŕšťúṽŵẋýž";
        private const string UpperAccents = "ÀƁÇĎÉƑĜĤÍĴĶĹṀÑÖƤɊŔŠŤÚṼŴẊÝŽ";

        // Padding chars used by Elongate — visually distinct so it's obvious.
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
            bool accents  = (mode & PseudoLocaleMode.Accents)  != 0;
            bool elongate = (mode & PseudoLocaleMode.Elongate) != 0;
            bool brackets = (mode & PseudoLocaleMode.Brackets) != 0;
            bool mirror   = (mode & PseudoLocaleMode.Mirror)   != 0;

            // Calculate required capacity to avoid reallocation
            int extraLen = 0;
            if (brackets) extraLen += 2; // [「 and 」]
            if (elongate) extraLen += (input.Length + 2) / 3; // ~33% padding

            // Use stackalloc for small strings to avoid heap allocation.
            // Threshold 512 chars covers >99% of UI strings.
            int maxLen = input.Length + extraLen;
            Span<char> buffer = maxLen <= 512
                ? stackalloc char[maxLen]
                : new char[maxLen];

            int pos = 0;

            // Opening bracket
            if (brackets)
                buffer[pos++] = '⟦';

            // Core transform
            if (mirror)
            {
                // Reverse iteration — combined with accents if both enabled
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

            // Elongation padding (~33% of original length, minimum 1)
            if (elongate)
            {
                int padCount = Math.Max(1, (input.Length + 2) / 3);
                for (int i = 0; i < padCount; i++)
                    buffer[pos++] = '~';
            }

            // Closing bracket
            if (brackets)
                buffer[pos++] = '⟧';

            return new string(buffer.Slice(0, pos));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static char AccentChar(char c)
        {
            if (c >= 'a' && c <= 'z') return LowerAccents[c - 'a'];
            if (c >= 'A' && c <= 'Z') return UpperAccents[c - 'A'];
            return c; // digits, punctuation, CJK, format placeholders — untouched
        }
    }
}
