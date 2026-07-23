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
        private const int MaxLocationLength = 4096;

        [SerializeField] private AudioLocationKind locationKind = AudioLocationKind.FilePath;
        [SerializeField, FormerlySerializedAs("location")] internal string m_Location = string.Empty;
        [SerializeField] internal string m_GUID = string.Empty;
        [SerializeField] private bool runtimeMutable;
        [SerializeField] private int version;

        public AudioLocationKind LocationKind
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipReference) + ".LocationKind");
                return locationKind;
            }
        }

        public string Location
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipReference) + ".Location");
                return m_Location;
            }
        }

        public string GUID
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipReference) + ".GUID");
                return m_GUID;
            }
        }

        public bool RuntimeMutable
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipReference) + ".RuntimeMutable");
                return runtimeMutable;
            }
        }

        public int Version
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipReference) + ".Version");
                return version;
            }
        }

        public bool HasEditorAssetLink
        {
            get
            {
                AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipReference) + ".HasEditorAssetLink");
                return !string.IsNullOrEmpty(m_GUID);
            }
        }

        /// <summary>
        /// Creates a non-persistent reference for runtime configuration. The caller owns the
        /// returned ScriptableObject and must destroy it on the Unity main thread.
        /// </summary>
        public static AudioClipReference CreateRuntime(AudioLocationKind kind, string location, string guid = null)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipReference) + ".CreateRuntime");
            var reference = CreateInstance<AudioClipReference>();
            reference.hideFlags = HideFlags.DontSave;
            reference.runtimeMutable = true;
            reference.locationKind = kind;
            reference.m_Location = location ?? string.Empty;
            reference.m_GUID = guid ?? string.Empty;
            reference.version = 1;
            return reference;
        }

        public void SetLocation(string newLocation)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipReference) + ".SetLocation");
            if (!runtimeMutable)
            {
                throw new InvalidOperationException(
                    "Authored AudioClipReference assets are immutable at runtime. " +
                    "Use AudioClipReference.CreateRuntime or TrySetLocation on a runtime-mutable reference.");
            }

            string normalized = newLocation ?? string.Empty;
            if (m_Location == normalized && string.IsNullOrEmpty(m_GUID))
            {
                return;
            }

            m_Location = normalized;
            m_GUID = string.Empty;
            IncrementVersion();
        }

        public void SetAssetLocation(string newLocation, string guid)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipReference) + ".SetAssetLocation");
            if (!runtimeMutable)
            {
                throw new InvalidOperationException(
                    "Authored AudioClipReference assets are immutable at runtime. " +
                    "Use AudioClipReference.CreateRuntime or TrySetLocation on a runtime-mutable reference.");
            }

            string normalizedLocation = newLocation ?? string.Empty;
            string normalizedGuid = guid ?? string.Empty;
            if (m_Location == normalizedLocation && m_GUID == normalizedGuid)
            {
                return;
            }

            m_Location = normalizedLocation;
            m_GUID = normalizedGuid;
            IncrementVersion();
        }

        public bool TrySetLocation(AudioLocationKind kind, string newLocation, string guid = null)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipReference) + ".TrySetLocation");
            if (!runtimeMutable) return false;

            string normalizedLocation = newLocation ?? string.Empty;
            string normalizedGuid = guid ?? string.Empty;
            if (locationKind == kind && m_Location == normalizedLocation && m_GUID == normalizedGuid)
                return true;

            locationKind = kind;
            m_Location = normalizedLocation;
            m_GUID = normalizedGuid;
            IncrementVersion();
            return true;
        }

        public string ResolveLocation()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipReference) + ".ResolveLocation");
            return TryResolveLocation(out string resolvedLocation, out _) ? resolvedLocation : string.Empty;
        }

        public bool TryResolveLocation(out string resolvedLocation, out string error)
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipReference) + ".TryResolveLocation");
            resolvedLocation = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(m_Location))
            {
                error = "Location is empty.";
                return false;
            }

            if (m_Location.Length > MaxLocationLength)
            {
                error = $"Location exceeds the {MaxLocationLength}-character limit.";
                return false;
            }

            if (HasNullCharacter(m_Location))
            {
                error = "Location contains a null character.";
                return false;
            }

            switch (locationKind)
            {
                case AudioLocationKind.StreamingAssetsPath:
                    if (!TryResolveRootedLocation(Application.streamingAssetsPath, m_Location, out resolvedLocation))
                    {
                        error = "StreamingAssets location must be a safe relative path within StreamingAssets.";
                        return false;
                    }
                    return true;
                case AudioLocationKind.PersistentDataPath:
                    if (!TryResolveRootedLocation(Application.persistentDataPath, m_Location, out resolvedLocation))
                    {
                        error = "PersistentData location must be a safe relative path within persistentDataPath.";
                        return false;
                    }
                    return true;
                case AudioLocationKind.Url:
                    if (!Uri.TryCreate(m_Location, UriKind.Absolute, out Uri uri) ||
                        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    {
                        error = "URL must be an absolute HTTP or HTTPS URI.";
                        return false;
                    }
                    resolvedLocation = m_Location;
                    return true;
                default:
                    resolvedLocation = m_Location;
                    return true;
            }
        }

        public string GetDisplayLocation()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioClipReference) + ".GetDisplayLocation");
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

            // URI-like StreamingAssets roots are resolved by UnityWebRequest on some
            // platforms. Reject query, fragment, and percent-encoded path syntax so a
            // provider cannot reinterpret an apparently relative path after validation.
            if (location.IndexOf('?') >= 0 || location.IndexOf('#') >= 0 || location.IndexOf('%') >= 0)
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

        private void IncrementVersion()
        {
            version = version == int.MaxValue ? 1 : version + 1;
        }
    }
}
