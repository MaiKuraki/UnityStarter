using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Provider-neutral reference to a runtime UI asset.
    /// The location is the runtime contract. The GUID is Editor-only authoring metadata
    /// and must never be used as a runtime address.
    /// </summary>
    [Serializable]
    public struct UIAssetReference : IEquatable<UIAssetReference>
    {
        [FormerlySerializedAs("m_Location")]
        [SerializeField] private string location;

        [FormerlySerializedAs("m_GUID")]
        [SerializeField] private string editorGuid;

        public UIAssetReference(string location, string editorGuid = null)
        {
            this.location = location;
            this.editorGuid = editorGuid;
        }

        public string Location => location ?? string.Empty;
        public string EditorGuid => editorGuid ?? string.Empty;
        public bool IsValid => !string.IsNullOrWhiteSpace(location);

        public bool Equals(UIAssetReference other)
        {
            return string.Equals(location, other.location, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is UIAssetReference other && Equals(other);

        public override int GetHashCode()
        {
            return location != null ? StringComparer.Ordinal.GetHashCode(location) : 0;
        }

        public static bool operator ==(UIAssetReference left, UIAssetReference right) => left.Equals(right);
        public static bool operator !=(UIAssetReference left, UIAssetReference right) => !left.Equals(right);
    }
}
