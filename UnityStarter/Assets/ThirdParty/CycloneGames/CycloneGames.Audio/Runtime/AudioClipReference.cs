// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
using UnityEngine.Serialization;

namespace CycloneGames.Audio.Runtime
{
    public enum AudioLocationKind
    {
        FilePath = 0,
        StreamingAssetsPath = 1,
        PersistentDataPath = 2,
        Url = 3,
        AssetAddress = 4
    }

    [CreateAssetMenu(menuName = "CycloneGames/Audio/Audio Clip Reference")]
    public sealed class AudioClipReference : ScriptableObject
    {
        [SerializeField] private AudioLocationKind locationKind = AudioLocationKind.FilePath;
        [SerializeField, FormerlySerializedAs("location")] internal string m_Location = string.Empty;
        [SerializeField] internal string m_GUID = string.Empty;
        [SerializeField] private bool runtimeMutable = true;
        [SerializeField] private int version;

        public AudioLocationKind LocationKind => locationKind;
        public string Location => m_Location;
        public string GUID => m_GUID;
        public bool RuntimeMutable => runtimeMutable;
        public int Version => version;

        public void SetLocation(string newLocation)
        {
            if (!runtimeMutable) return;

            string normalized = newLocation ?? string.Empty;
            if (m_Location == normalized && string.IsNullOrEmpty(m_GUID)) return;

            m_Location = normalized;
            m_GUID = string.Empty;
            version++;
        }

        public void SetAssetLocation(string newLocation, string guid)
        {
            string normalizedLocation = newLocation ?? string.Empty;
            string normalizedGuid = guid ?? string.Empty;
            if (m_Location == normalizedLocation && m_GUID == normalizedGuid) return;

            m_Location = normalizedLocation;
            m_GUID = normalizedGuid;
            version++;
        }

        public string ResolveLocation()
        {
            if (string.IsNullOrWhiteSpace(m_Location))
                return string.Empty;

            switch (locationKind)
            {
                case AudioLocationKind.StreamingAssetsPath:
                    return System.IO.Path.Combine(Application.streamingAssetsPath, m_Location);
                case AudioLocationKind.PersistentDataPath:
                    return System.IO.Path.Combine(Application.persistentDataPath, m_Location);
                default:
                    return m_Location;
            }
        }
    }
}
