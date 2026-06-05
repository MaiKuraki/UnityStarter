using System;
using System.Runtime.CompilerServices;
using CycloneGames.AssetManagement.Runtime;
using UnityEngine;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Serializable key that binds to a specific asset table entry.
    /// Resolves to a per-locale <see cref="AssetRef"/> at runtime via
    /// <see cref="ILocalizationService.ResolveAsset{T}(LocalizedAsset{T})"/>.
    /// </summary>
    [Serializable]
    public struct LocalizedAsset<T> : IEquatable<LocalizedAsset<T>> where T : UnityEngine.Object
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
            get => !string.IsNullOrEmpty(m_TableId) && !string.IsNullOrEmpty(m_EntryKey);
        }

        public LocalizedAsset(string tableId, string entryKey)
        {
            m_TableId = tableId;
            m_EntryKey = entryKey;
        }

        public readonly bool Equals(LocalizedAsset<T> other) =>
            string.Equals(m_EntryKey, other.m_EntryKey, StringComparison.Ordinal) &&
            string.Equals(m_TableId, other.m_TableId, StringComparison.Ordinal);

        public readonly override bool Equals(object obj) => obj is LocalizedAsset<T> other && Equals(other);
        public readonly override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (m_TableId != null ? StringComparer.Ordinal.GetHashCode(m_TableId) : 0);
                hash = hash * 31 + (m_EntryKey != null ? StringComparer.Ordinal.GetHashCode(m_EntryKey) : 0);
                return hash;
            }
        }
        public static bool operator ==(LocalizedAsset<T> a, LocalizedAsset<T> b) => a.Equals(b);
        public static bool operator !=(LocalizedAsset<T> a, LocalizedAsset<T> b) => !a.Equals(b);
        public readonly override string ToString() => $"{m_TableId}/{m_EntryKey}";
    }
}
