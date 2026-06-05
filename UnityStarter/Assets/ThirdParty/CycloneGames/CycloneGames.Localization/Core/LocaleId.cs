using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Localization.Core
{
    /// <summary>
    /// Immutable locale identifier backed by a BCP 47 code string.
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
            Code = code;
        }

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !string.IsNullOrEmpty(Code);
        }

        /// <summary>
        /// Returns the language-only portion: "zh-CN" -> "zh", "en" -> "en".
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
    }
}
