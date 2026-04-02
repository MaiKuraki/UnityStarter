using System;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// A serializable, type-safe reference to an asset managed by the asset management system.
    /// Stores only a string location and editor GUID — zero heap allocation at runtime (struct).
    /// <para>
    /// AssetRef is a pure data key. It does NOT hold a loaded handle or trigger loading on its own.
    /// Use <c>package.LoadAsync(assetRef)</c> to load the asset through an <see cref="IAssetPackage"/>.
    /// </para>
    /// </summary>
    [Serializable]
    public struct AssetRef<T> : IEquatable<AssetRef<T>> where T : UnityEngine.Object
    {
        [SerializeField] internal string m_Location;
        [SerializeField] internal string m_GUID;

        /// <summary>Runtime key used by the asset management system (e.g., asset path or address).</summary>
        public string Location => m_Location;

        /// <summary>Whether this reference points to a valid location.</summary>
        public bool IsValid => !string.IsNullOrEmpty(m_Location);

        public AssetRef(string location, string guid = null)
        {
            m_Location = location;
            m_GUID = guid;
        }

        /// <summary>Converts to the non-generic <see cref="AssetRef"/> form.</summary>
        public AssetRef Untyped() => new AssetRef(m_Location, m_GUID);

        public bool Equals(AssetRef<T> other) => string.Equals(m_Location, other.m_Location, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is AssetRef<T> other && Equals(other);
        public override int GetHashCode() => m_Location != null ? m_Location.GetHashCode() : 0;
        public static bool operator ==(AssetRef<T> a, AssetRef<T> b) => a.Equals(b);
        public static bool operator !=(AssetRef<T> a, AssetRef<T> b) => !a.Equals(b);
        public override string ToString() => m_Location ?? string.Empty;

        public static implicit operator string(AssetRef<T> r) => r.m_Location;
    }

    /// <summary>
    /// A non-generic, serializable reference to any asset managed by the asset management system.
    /// Use when the asset type is not known at compile time (e.g., data-driven config tables, runtime-resolved types).
    /// </summary>
    [Serializable]
    public struct AssetRef : IEquatable<AssetRef>
    {
        [SerializeField] internal string m_Location;
        [SerializeField] internal string m_GUID;

        /// <summary>Runtime key used by the asset management system.</summary>
        public string Location => m_Location;

        /// <summary>Whether this reference points to a valid location.</summary>
        public bool IsValid => !string.IsNullOrEmpty(m_Location);

        public AssetRef(string location, string guid = null)
        {
            m_Location = location;
            m_GUID = guid;
        }

        /// <summary>Converts to a typed <see cref="AssetRef{T}"/>.</summary>
        public AssetRef<T> Typed<T>() where T : UnityEngine.Object => new AssetRef<T>(m_Location, m_GUID);

        public bool Equals(AssetRef other) => string.Equals(m_Location, other.m_Location, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is AssetRef other && Equals(other);
        public override int GetHashCode() => m_Location != null ? m_Location.GetHashCode() : 0;
        public static bool operator ==(AssetRef a, AssetRef b) => a.Equals(b);
        public static bool operator !=(AssetRef a, AssetRef b) => !a.Equals(b);
        public override string ToString() => m_Location ?? string.Empty;

        public static implicit operator string(AssetRef r) => r.m_Location;
    }
}
