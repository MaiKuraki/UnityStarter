using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Immutable locale identifier backed by an interned string for O(1) equality via reference comparison.
    /// BCP 47 format: "en", "zh-CN", "ja-JP", etc.
    /// </summary>
    [Serializable]
    public readonly struct LocaleId : IEquatable<LocaleId>, IComparable<LocaleId>
    {
        public static readonly LocaleId Invalid = default;

        public readonly string Code;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LocaleId(string code)
        {
            Code = code != null ? string.Intern(code) : null;
        }

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Code != null;
        }

        /// <summary>
        /// Returns the language-only portion: "zh-CN" → "zh", "en" → "en".
        /// </summary>
        public LocaleId Language
        {
            get
            {
                if (Code == null) return Invalid;
                int dash = Code.IndexOf('-');
                return dash > 0 ? new LocaleId(Code.Substring(0, dash)) : this;
            }
        }

        // Reference equality via interned strings — no char-by-char comparison
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(LocaleId other) => ReferenceEquals(Code, other.Code);
        public override bool Equals(object obj) => obj is LocaleId other && Equals(other);
        public override int GetHashCode() => Code != null ? Code.GetHashCode() : 0;
        public int CompareTo(LocaleId other) => string.Compare(Code, other.Code, StringComparison.Ordinal);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(LocaleId a, LocaleId b) => ReferenceEquals(a.Code, b.Code);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(LocaleId a, LocaleId b) => !ReferenceEquals(a.Code, b.Code);

        public override string ToString() => Code ?? string.Empty;
        public static implicit operator string(LocaleId id) => id.Code;
    }
}
