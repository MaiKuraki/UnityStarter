using System;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// A serializable reference to a scene managed by the asset management system.
    /// <para>
    /// Identical storage layout to <see cref="AssetRef"/> but carries scene-specific semantics:
    /// the Editor PropertyDrawer filters for <c>SceneAsset</c> and the extension method bridges to
    /// <see cref="IAssetPackage.LoadSceneAsync"/> instead of <see cref="IAssetPackage.LoadAssetAsync{TAsset}"/>.
    /// </para>
    /// <para>
    /// SceneAsset is editor-only. At runtime, only <see cref="Location"/> is used as the scene address.
    /// </para>
    /// </summary>
    [Serializable]
    public struct SceneRef : IEquatable<SceneRef>
    {
        [SerializeField] internal string m_Location;
        [SerializeField] internal string m_GUID;

        /// <summary>Runtime key used by the asset management system to identify the scene.</summary>
        public string Location => m_Location;

        /// <summary>Whether this reference points to a valid location.</summary>
        public bool IsValid => !string.IsNullOrEmpty(m_Location);

        public SceneRef(string location, string guid = null)
        {
            m_Location = location;
            m_GUID = guid;
        }

        public bool Equals(SceneRef other) => string.Equals(m_Location, other.m_Location, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SceneRef other && Equals(other);
        public override int GetHashCode() => m_Location != null ? m_Location.GetHashCode() : 0;
        public static bool operator ==(SceneRef a, SceneRef b) => a.Equals(b);
        public static bool operator !=(SceneRef a, SceneRef b) => !a.Equals(b);
        public override string ToString() => m_Location ?? string.Empty;

        public static implicit operator string(SceneRef r) => r.m_Location;
    }
}
