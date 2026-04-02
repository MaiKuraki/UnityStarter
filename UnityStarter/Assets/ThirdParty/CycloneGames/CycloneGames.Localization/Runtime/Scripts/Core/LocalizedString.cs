using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Serializable key that binds to a specific string table entry.
    /// Inspector displays the resolved string for the current editor locale.
    /// At runtime, resolve via <see cref="ILocalizationService.GetString(LocalizedString)"/>.
    /// </summary>
    [Serializable]
    public struct LocalizedString : IEquatable<LocalizedString>
    {
        [SerializeField] internal string m_TableId;
        [SerializeField] internal string m_EntryKey;

        public readonly string TableId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_TableId;
        }

        public readonly string EntryKey
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_EntryKey;
        }

        public readonly bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_EntryKey != null;
        }

        public LocalizedString(string tableId, string entryKey)
        {
            m_TableId = tableId;
            m_EntryKey = entryKey;
        }

        public readonly bool Equals(LocalizedString other) =>
            string.Equals(m_EntryKey, other.m_EntryKey, StringComparison.Ordinal) &&
            string.Equals(m_TableId, other.m_TableId, StringComparison.Ordinal);

        public readonly override bool Equals(object obj) => obj is LocalizedString other && Equals(other);
        public readonly override int GetHashCode() => m_EntryKey?.GetHashCode() ?? 0;
        public static bool operator ==(LocalizedString a, LocalizedString b) => a.Equals(b);
        public static bool operator !=(LocalizedString a, LocalizedString b) => !a.Equals(b);
        public readonly override string ToString() => $"{m_TableId}/{m_EntryKey}";
    }
}
