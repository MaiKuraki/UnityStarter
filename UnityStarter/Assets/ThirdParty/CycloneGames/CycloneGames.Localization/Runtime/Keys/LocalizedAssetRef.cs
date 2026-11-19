using System;
using UnityEngine;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Serializable asset key that stores a runtime location and an Editor authoring GUID.
    /// It does not hold a loaded handle.
    /// </summary>

    [Serializable]
    public struct LocalizedAssetRef<T> : IEquatable<LocalizedAssetRef<T>>
        where T : UnityEngine.Object
    {
        [SerializeField] internal string m_Location;
        [SerializeField] internal string m_GUID;

        public string Location => m_Location;
        public string Guid => m_GUID;
        public bool IsValid => !string.IsNullOrEmpty(m_Location);

        public LocalizedAssetRef(string location, string guid = null)
        {
            m_Location = location;
            m_GUID = guid;
        }

        public LocalizedAssetRef Untyped() => new LocalizedAssetRef(m_Location, m_GUID);

        public bool Equals(LocalizedAssetRef<T> other) =>
            string.Equals(m_Location, other.m_Location, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is LocalizedAssetRef<T> other && Equals(other);

        public override int GetHashCode() =>
            m_Location != null ? StringComparer.Ordinal.GetHashCode(m_Location) : 0;

        public static bool operator ==(LocalizedAssetRef<T> left, LocalizedAssetRef<T> right) =>
            left.Equals(right);

        public static bool operator !=(LocalizedAssetRef<T> left, LocalizedAssetRef<T> right) =>
            !left.Equals(right);

        public override string ToString() => m_Location ?? string.Empty;

        public static implicit operator string(LocalizedAssetRef<T> reference) => reference.m_Location;
    }

    /// <summary>
    /// Non-generic, serializable asset key used by data-driven catalog and table entries.
    /// </summary>
    [Serializable]
    public struct LocalizedAssetRef : IEquatable<LocalizedAssetRef>
    {
        [SerializeField] internal string m_Location;
        [SerializeField] internal string m_GUID;

        public string Location => m_Location;
        public string Guid => m_GUID;
        public bool IsValid => !string.IsNullOrEmpty(m_Location);

        public LocalizedAssetRef(string location, string guid = null)
        {
            m_Location = location;
            m_GUID = guid;
        }

        public LocalizedAssetRef<T> Typed<T>() where T : UnityEngine.Object =>
            new LocalizedAssetRef<T>(m_Location, m_GUID);

        public bool Equals(LocalizedAssetRef other) =>
            string.Equals(m_Location, other.m_Location, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is LocalizedAssetRef other && Equals(other);

        public override int GetHashCode() =>
            m_Location != null ? StringComparer.Ordinal.GetHashCode(m_Location) : 0;

        public static bool operator ==(LocalizedAssetRef left, LocalizedAssetRef right) =>
            left.Equals(right);

        public static bool operator !=(LocalizedAssetRef left, LocalizedAssetRef right) =>
            !left.Equals(right);

        public override string ToString() => m_Location ?? string.Empty;

        public static implicit operator string(LocalizedAssetRef reference) => reference.m_Location;
    }
}
