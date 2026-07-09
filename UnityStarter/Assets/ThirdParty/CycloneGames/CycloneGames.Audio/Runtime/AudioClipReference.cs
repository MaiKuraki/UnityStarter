// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
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
        public bool HasEditorAssetLink => !string.IsNullOrEmpty(m_GUID);

        public void SetLocation(string newLocation)
        {
            if (!runtimeMutable)
            {
                return;
            }

            string normalized = newLocation ?? string.Empty;
            if (m_Location == normalized && string.IsNullOrEmpty(m_GUID))
            {
                return;
            }

            m_Location = normalized;
            m_GUID = string.Empty;
            version++;
        }

        public void SetAssetLocation(string newLocation, string guid)
        {
            string normalizedLocation = newLocation ?? string.Empty;
            string normalizedGuid = guid ?? string.Empty;
            if (m_Location == normalizedLocation && m_GUID == normalizedGuid)
            {
                return;
            }

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
                    return TryResolveRootedLocation(Application.streamingAssetsPath, m_Location, out string streamingAssetsLocation)
                        ? streamingAssetsLocation
                        : string.Empty;
                case AudioLocationKind.PersistentDataPath:
                    return TryResolveRootedLocation(Application.persistentDataPath, m_Location, out string persistentDataLocation)
                        ? persistentDataLocation
                        : string.Empty;
                default:
                    return m_Location;
            }
        }

        public string GetDisplayLocation()
        {
            return m_Location ?? string.Empty;
        }

        private static bool TryResolveRootedLocation(string rootPath, string location, out string resolvedLocation)
        {
            resolvedLocation = string.Empty;

            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(location))
            {
                return false;
            }

            if (HasNullCharacter(location))
            {
                return false;
            }

            if (IsUriLikeRoot(rootPath))
            {
                if (!IsSafeRelativePath(location))
                {
                    return false;
                }

                resolvedLocation = CombineUriLikePath(rootPath, location);
                return true;
            }

            try
            {
                string normalizedRoot = Path.GetFullPath(rootPath);
                string normalizedLocation = Path.GetFullPath(Path.Combine(normalizedRoot, location));

                if (!IsPathWithinRoot(normalizedRoot, normalizedLocation))
                {
                    return false;
                }

                resolvedLocation = normalizedLocation;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (PathTooLongException)
            {
                return false;
            }
        }

        private static bool IsPathWithinRoot(string rootPath, string candidatePath)
        {
            StringComparison comparison = GetPathComparison();
            if (string.Equals(rootPath, candidatePath, comparison))
            {
                return true;
            }

            string rootWithSeparator = EnsureTrailingSeparator(rootPath);
            return candidatePath.StartsWith(rootWithSeparator, comparison);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            char lastCharacter = path[path.Length - 1];
            if (lastCharacter == Path.DirectorySeparatorChar || lastCharacter == Path.AltDirectorySeparatorChar)
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static StringComparison GetPathComparison()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return StringComparison.OrdinalIgnoreCase;
#else
            return StringComparison.Ordinal;
#endif
        }

        private static bool IsUriLikeRoot(string rootPath)
        {
            return rootPath.IndexOf("://", StringComparison.Ordinal) >= 0 ||
                   rootPath.StartsWith("jar:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSafeRelativePath(string location)
        {
            if (Path.IsPathRooted(location) || HasNullCharacter(location))
            {
                return false;
            }

            int segmentStart = 0;
            for (int i = 0; i <= location.Length; i++)
            {
                bool atSeparator = i == location.Length || location[i] == '/' || location[i] == '\\';
                if (!atSeparator)
                {
                    continue;
                }

                if (i - segmentStart == 2 && location[segmentStart] == '.' && location[segmentStart + 1] == '.')
                {
                    return false;
                }

                segmentStart = i + 1;
            }

            return true;
        }

        private static string CombineUriLikePath(string rootPath, string location)
        {
            string normalizedRoot = rootPath.EndsWith("/", StringComparison.Ordinal)
                ? rootPath
                : rootPath + "/";
            string normalizedLocation = location.Replace('\\', '/').TrimStart('/');
            return normalizedRoot + normalizedLocation;
        }

        private static bool HasNullCharacter(string value)
        {
            return value.IndexOf('\0') >= 0;
        }
    }
}
