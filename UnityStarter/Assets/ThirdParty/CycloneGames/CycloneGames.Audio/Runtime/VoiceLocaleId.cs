// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Immutable, canonical voice-locale identifier using a bounded BCP 47-like syntax.
    /// This type deliberately does not depend on CycloneGames.Localization.
    /// </summary>
    public readonly struct VoiceLocaleId : IEquatable<VoiceLocaleId>, IComparable<VoiceLocaleId>
    {
        public const int MaxCodeLength = 63;
        public const int MaxSubtagCount = 8;

        public static readonly VoiceLocaleId Invalid = default;

        public readonly string Code;

        public VoiceLocaleId(string code)
        {
            Code = TryCanonicalize(code, out string canonicalCode)
                ? canonicalCode
                : null;
        }

        private VoiceLocaleId(string canonicalCode, bool isCanonical)
        {
            Code = isCanonical ? canonicalCode : null;
        }

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Code != null;
        }

        public static bool TryCreate(string code, out VoiceLocaleId localeId)
        {
            if (!TryCanonicalize(code, out string canonicalCode))
            {
                localeId = Invalid;
                return false;
            }

            localeId = new VoiceLocaleId(canonicalCode, true);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(VoiceLocaleId other) =>
            string.Equals(Code, other.Code, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is VoiceLocaleId other && Equals(other);

        public override int GetHashCode() =>
            Code != null ? StringComparer.Ordinal.GetHashCode(Code) : 0;

        public int CompareTo(VoiceLocaleId other) =>
            string.Compare(Code, other.Code, StringComparison.Ordinal);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(VoiceLocaleId left, VoiceLocaleId right) =>
            left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(VoiceLocaleId left, VoiceLocaleId right) =>
            !left.Equals(right);

        public override string ToString() => Code ?? string.Empty;

        public static implicit operator string(VoiceLocaleId id) => id.Code;

        private static bool TryCanonicalize(string code, out string canonicalCode)
        {
            canonicalCode = null;
            if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength)
                return false;

            int subtagCount = 1;
            int subtagStart = 0;
            char[] canonical = null;

            for (int i = 0; i <= code.Length; i++)
            {
                bool atEnd = i == code.Length;
                if (!atEnd && code[i] != '-')
                    continue;

                int length = i - subtagStart;
                if (length == 0 || length > 8 || subtagCount > MaxSubtagCount)
                    return false;

                if (subtagCount == 1)
                {
                    if (length < 2 || !IsAsciiLetters(code, subtagStart, length))
                        return false;
                }
                else if (!IsAsciiAlphaNumeric(code, subtagStart, length))
                {
                    return false;
                }

                for (int index = subtagStart; index < i; index++)
                {
                    char source = code[index];
                    char target = CanonicalizeCharacter(
                        code,
                        subtagCount,
                        subtagStart,
                        length,
                        index,
                        source);
                    if (source == target)
                        continue;

                    if (canonical == null)
                        canonical = code.ToCharArray();
                    canonical[index] = target;
                }

                if (!atEnd)
                {
                    subtagCount++;
                    subtagStart = i + 1;
                }
            }

            canonicalCode = canonical == null ? code : new string(canonical);
            return true;
        }

        private static char CanonicalizeCharacter(
            string code,
            int subtagNumber,
            int subtagStart,
            int subtagLength,
            int index,
            char value)
        {
            if (subtagNumber == 1)
                return ToLowerAscii(value);

            bool script =
                subtagLength == 4 && IsAsciiLetters(code, subtagStart, subtagLength);
            if (script)
                return index == subtagStart ? ToUpperAscii(value) : ToLowerAscii(value);

            bool region =
                (subtagLength == 2 && IsAsciiLetters(code, subtagStart, subtagLength)) ||
                (subtagLength == 3 && IsAsciiDigits(code, subtagStart, subtagLength));
            if (region)
                return ToUpperAscii(value);

            return ToLowerAscii(value);
        }

        private static bool IsAsciiLetters(string value, int start, int length)
        {
            for (int i = start; i < start + length; i++)
            {
                char character = value[i];
                if ((character < 'A' || character > 'Z') &&
                    (character < 'a' || character > 'z'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAsciiDigits(string value, int start, int length)
        {
            for (int i = start; i < start + length; i++)
            {
                char character = value[i];
                if (character < '0' || character > '9')
                    return false;
            }

            return true;
        }

        private static bool IsAsciiAlphaNumeric(string value, int start, int length)
        {
            for (int i = start; i < start + length; i++)
            {
                char character = value[i];
                bool letter =
                    (character >= 'A' && character <= 'Z') ||
                    (character >= 'a' && character <= 'z');
                if (!letter && (character < '0' || character > '9'))
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static char ToLowerAscii(char value) =>
            value >= 'A' && value <= 'Z' ? (char)(value + ('a' - 'A')) : value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static char ToUpperAscii(char value) =>
            value >= 'a' && value <= 'z' ? (char)(value - ('a' - 'A')) : value;
    }
}
