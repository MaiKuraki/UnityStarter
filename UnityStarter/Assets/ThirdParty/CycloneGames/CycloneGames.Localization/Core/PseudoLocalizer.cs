using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace CycloneGames.Localization.Core
{
    /// <summary>
    /// Pure-function pseudo-localizer that preserves composite-format items and rich-text tags.
    /// Transforming enabled text creates a new string; disabled mode returns the original reference.
    /// </summary>
    public static class PseudoLocalizer
    {
        private const string LowerAccents =
            "\u00E0\u1E03\u00E7\u010F\u00E9\u0192\u011F\u0125\u00ED\u0135\u0137\u013A\u1E3F" +
            "\u00F1\u00F6\u1E57\u01EB\u0155\u015F\u0163\u00FA\u1E7D\u0175\u1E8B\u00FD\u017E";

        private const string UpperAccents =
            "\u00C0\u1E02\u00C7\u010E\u00C9\u0191\u011E\u0124\u00CD\u0134\u0136\u0139\u1E3E" +
            "\u00D1\u00D6\u1E56\u01EA\u0154\u015E\u0162\u00DA\u1E7C\u0174\u1E8A\u00DD\u017D";

        /// <summary>
        /// Transforms a resolved string according to the active pseudo-locale mode.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Transform(string input, PseudoLocaleMode mode)
        {
            if (mode == PseudoLocaleMode.None || string.IsNullOrEmpty(input))
                return input;

            return TransformCore(input, mode);
        }

        private static string TransformCore(string input, PseudoLocaleMode mode)
        {
            bool accents = (mode & PseudoLocaleMode.Accents) != 0;
            bool elongate = (mode & PseudoLocaleMode.Elongate) != 0;
            bool brackets = (mode & PseudoLocaleMode.Brackets) != 0;
            bool mirror = (mode & PseudoLocaleMode.Mirror) != 0;

            int capacity = input.Length <= int.MaxValue - 16 ? input.Length + 16 : input.Length;
            var output = new StringBuilder(capacity);
            if (brackets)
                output.Append('\u27E6');

            int visibleElementCount = 0;
            int textStart = 0;
            int index = 0;

            while (index < input.Length)
            {
                int tokenEnd = -1;
                char current = input[index];

                if (current == '<')
                    tokenEnd = FindRichTextTokenEnd(input, index);
                else if (current == '{' || current == '}')
                    tokenEnd = FindCompositeFormatTokenEnd(input, index);

                if (tokenEnd < 0)
                {
                    index++;
                    continue;
                }

                AppendTextSegment(output, input, textStart, index - textStart, accents, mirror, ref visibleElementCount);
                output.Append(input, index, tokenEnd - index + 1);
                index = tokenEnd + 1;
                textStart = index;
            }

            AppendTextSegment(
                output,
                input,
                textStart,
                input.Length - textStart,
                accents,
                mirror,
                ref visibleElementCount);

            if (elongate)
            {
                int padCount = Math.Max(1, (visibleElementCount + 2) / 3);
                output.Append('~', padCount);
            }

            if (brackets)
                output.Append('\u27E7');

            return output.ToString();
        }

        private static int FindRichTextTokenEnd(string input, int start)
        {
            if (start + 2 >= input.Length)
                return -1;

            char first = input[start + 1];
            if (first != '/' && first != '!' && first != '#' && !IsAsciiLetter(first))
                return -1;

            char quote = '\0';
            for (int i = start + 2; i < input.Length; i++)
            {
                char c = input[i];
                if (quote != '\0')
                {
                    if (c == quote)
                        quote = '\0';
                    continue;
                }

                if (c == '\'' || c == '"')
                {
                    quote = c;
                    continue;
                }

                if (c == '>')
                    return i;
            }

            return -1;
        }

        private static int FindCompositeFormatTokenEnd(string input, int start)
        {
            char opening = input[start];
            if (start + 1 < input.Length && input[start + 1] == opening)
                return start + 1;

            if (opening == '}')
                return -1;

            int index = start + 1;
            while (index < input.Length && char.IsWhiteSpace(input[index]))
                index++;

            int digitStart = index;
            while (index < input.Length && input[index] >= '0' && input[index] <= '9')
                index++;

            if (index == digitStart)
                return -1;

            for (; index < input.Length; index++)
            {
                char c = input[index];
                if (c == '}' && (index + 1 >= input.Length || input[index + 1] != '}'))
                    return index;

                if (c == '{' && (index + 1 >= input.Length || input[index + 1] != '{'))
                    return -1;

                if ((c == '{' || c == '}') && index + 1 < input.Length && input[index + 1] == c)
                    index++;
            }

            return -1;
        }

        private static void AppendTextSegment(
            StringBuilder output,
            string input,
            int start,
            int length,
            bool accents,
            bool mirror,
            ref int visibleElementCount)
        {
            if (length <= 0)
                return;

            if (!mirror)
            {
                int end = start + length;
                for (int i = start; i < end; i++)
                {
                    char c = input[i];
                    output.Append(accents ? AccentCharacter(c) : c);
                    if (char.IsLowSurrogate(c)) continue;

                    UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(input, i);
                    if (category != UnicodeCategory.NonSpacingMark &&
                        category != UnicodeCategory.SpacingCombiningMark &&
                        category != UnicodeCategory.EnclosingMark)
                    {
                        visibleElementCount++;
                    }
                }

                return;
            }

            var starts = new List<int>(Math.Min(length, 32));
            var reverseEnumerator = StringInfo.GetTextElementEnumerator(input, start);
            int segmentEnd = start + length;
            while (reverseEnumerator.MoveNext() && reverseEnumerator.ElementIndex < segmentEnd)
                starts.Add(reverseEnumerator.ElementIndex);

            visibleElementCount += starts.Count;
            for (int i = starts.Count - 1; i >= 0; i--)
            {
                int elementStart = starts[i];
                int elementEnd = i + 1 < starts.Count ? starts[i + 1] : segmentEnd;
                AppendTextElement(output, input, elementStart, elementEnd - elementStart, accents);
            }
        }

        private static void AppendTextElement(StringBuilder output, string input, int start, int length, bool accents)
        {
            int end = start + length;
            for (int i = start; i < end; i++)
            {
                char c = input[i];
                output.Append(accents ? AccentCharacter(c) : c);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static char AccentCharacter(char c)
        {
            if (c >= 'a' && c <= 'z') return LowerAccents[c - 'a'];
            if (c >= 'A' && c <= 'Z') return UpperAccents[c - 'A'];
            return c;
        }
    }
}
