using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Localization.Core
{
    /// <summary>
    /// Immutable, canonical locale identifier using a bounded BCP 47-like syntax.
    /// </summary>
    [Serializable]
    public readonly struct LocaleId : IEquatable<LocaleId>, IComparable<LocaleId>
    {
        public const int MaxCodeLength = 63;
        public const int MaxSubtagCount = 8;

        public static readonly LocaleId Invalid = default;

        public readonly string Code;

        private readonly string _languageCode;

        /// <summary>
        /// Creates a canonical locale identifier. Invalid input produces <see cref="Invalid"/>.
        /// Use <see cref="TryCreate"/> when invalid input must be distinguished explicitly.
        /// </summary>
        public LocaleId(string code)
        {
            if (!TryCanonicalize(code, out string canonicalCode, out string languageCode))
            {
                Code = null;
                _languageCode = null;
                return;
            }

            Code = canonicalCode;
            _languageCode = languageCode;
        }

        private LocaleId(string canonicalCode, string languageCode)
        {
            Code = canonicalCode;
            _languageCode = languageCode;
        }

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Code != null;
        }

        /// <summary>
        /// Returns the canonical primary language subtag without allocating.
        /// </summary>
        public LocaleId Language
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _languageCode == null ? Invalid : new LocaleId(_languageCode, _languageCode);
        }

        /// <summary>
        /// Tries to validate and canonicalize a locale code.
        /// </summary>
        public static bool TryCreate(string code, out LocaleId localeId)
        {
            if (!TryCanonicalize(code, out string canonicalCode, out string languageCode))
            {
                localeId = Invalid;
                return false;
            }

            localeId = new LocaleId(canonicalCode, languageCode);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(LocaleId other) => string.Equals(Code, other.Code, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is LocaleId other && Equals(other);

        public override int GetHashCode() => Code != null ? StringComparer.Ordinal.GetHashCode(Code) : 0;

        public int CompareTo(LocaleId other) => string.Compare(Code, other.Code, StringComparison.Ordinal);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(LocaleId a, LocaleId b) => a.Equals(b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(LocaleId a, LocaleId b) => !a.Equals(b);

        public override string ToString() => Code ?? string.Empty;

        public static implicit operator string(LocaleId id) => id.Code;

        private static bool TryCanonicalize(string code, out string canonicalCode, out string languageCode)
        {
            canonicalCode = null;
            languageCode = null;

            if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength)
                return false;

            int subtagCount = 1;
            int subtagStart = 0;
            int languageLength = 0;
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

                    languageLength = length;
                }
                else if (!IsAsciiAlphaNumeric(code, subtagStart, length))
                {
                    return false;
                }

                for (int index = subtagStart; index < i; index++)
                {
                    char source = code[index];
                    char target = CanonicalizeCharacter(code, subtagCount, subtagStart, length, index, source);
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
            if (languageLength == canonicalCode.Length)
            {
                languageCode = canonicalCode;
            }
            else
            {
                char[] language = new char[languageLength];
                canonicalCode.CopyTo(0, language, 0, languageLength);
                languageCode = new string(language);
            }

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

            bool script = subtagLength == 4 && IsAsciiLetters(code, subtagStart, subtagLength);
            if (script)
                return index == subtagStart ? ToUpperAscii(value) : ToLowerAscii(value);

            bool region = (subtagLength == 2 && IsAsciiLetters(code, subtagStart, subtagLength)) ||
                          (subtagLength == 3 && IsAsciiDigits(code, subtagStart, subtagLength));
            if (region)
                return ToUpperAscii(value);

            return ToLowerAscii(value);
        }

        private static bool IsAsciiLetters(string value, int start, int length)
        {
            for (int i = start; i < start + length; i++)
            {
                char c = value[i];
                if ((c < 'A' || c > 'Z') && (c < 'a' || c > 'z'))
                    return false;
            }

            return true;
        }

        private static bool IsAsciiDigits(string value, int start, int length)
        {
            for (int i = start; i < start + length; i++)
            {
                char c = value[i];
                if (c < '0' || c > '9')
                    return false;
            }

            return true;
        }

        private static bool IsAsciiAlphaNumeric(string value, int start, int length)
        {
            for (int i = start; i < start + length; i++)
            {
                char c = value[i];
                bool letter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
                if (!letter && (c < '0' || c > '9'))
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static char ToLowerAscii(char value)
        {
            return value >= 'A' && value <= 'Z' ? (char)(value + ('a' - 'A')) : value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static char ToUpperAscii(char value)
        {
            return value >= 'a' && value <= 'z' ? (char)(value - ('a' - 'A')) : value;
        }
    }
}
