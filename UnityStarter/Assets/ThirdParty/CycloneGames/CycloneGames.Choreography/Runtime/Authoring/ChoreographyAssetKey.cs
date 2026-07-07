using System;
using UnityEngine;

namespace CycloneGames.Choreography
{
    /// <summary>
    /// Serializable asset-system key used by Choreography authoring without depending on a concrete loader.
    /// It stores a stable runtime location plus an editor GUID so integrations can map it to their own handle type.
    /// </summary>
    [Serializable]
    public struct ChoreographyAssetKey : IEquatable<ChoreographyAssetKey>
    {
        [SerializeField] internal string m_Location;
        [SerializeField] internal string m_GUID;

        public string Location => m_Location;
        public string Guid => m_GUID;

        public bool IsValid => !string.IsNullOrEmpty(m_Location);

        public ChoreographyAssetKey(string location, string guid = null)
        {
            m_Location = location;
            m_GUID = guid;
        }

        public bool Equals(ChoreographyAssetKey other)
        {
            return string.Equals(m_Location, other.m_Location, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ChoreographyAssetKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return m_Location != null ? m_Location.GetHashCode() : 0;
        }

        public override string ToString()
        {
            return m_Location ?? string.Empty;
        }

        public static bool operator ==(ChoreographyAssetKey left, ChoreographyAssetKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ChoreographyAssetKey left, ChoreographyAssetKey right)
        {
            return !left.Equals(right);
        }
    }
}
