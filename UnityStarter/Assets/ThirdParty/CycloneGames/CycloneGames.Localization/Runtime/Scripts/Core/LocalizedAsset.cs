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
            get => m_EntryKey != null;
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
        public readonly override int GetHashCode() => m_EntryKey?.GetHashCode() ?? 0;
        public static bool operator ==(LocalizedAsset<T> a, LocalizedAsset<T> b) => a.Equals(b);
        public static bool operator !=(LocalizedAsset<T> a, LocalizedAsset<T> b) => !a.Equals(b);
        public readonly override string ToString() => $"{m_TableId}/{m_EntryKey}";
    }
}
