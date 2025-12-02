#if ADDRESSABLES_PRESENT
using System.IO;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Helper class for managing Addressables version file paths.
    /// remote server first, then persistent data path, finally streaming assets.
    /// </summary>
    internal static class AddressablesVersionPathHelper
    {
        private const string VERSION_FILE_NAME = "AddressablesVersion.json";
        private const string ADDRESSABLES_CACHE_FOLDER = "com.unity.addressables";

        /// <summary>
        /// Gets the persistent data path for version file (writable, for hot updates).
        /// This is where downloaded version info should be saved.
        /// </summary>
        public static string GetPersistentVersionPath()
        {
            string cacheRoot = GetAddressablesCacheRoot();
            return Path.Combine(cacheRoot, VERSION_FILE_NAME);
        }

        /// <summary>
        /// Gets the streaming assets path for version file (read-only, initial version).
        /// Addressables stores content in StreamingAssets/aa/&lt;Platform&gt; structure.
        /// This method returns the expected path based on current platform.
        /// The actual file existence should be checked by the caller.
        /// </summary>
        public static string GetStreamingAssetsVersionPath()
        {
            // Addressables stores content in StreamingAssets/aa/<Platform>
            // Try platform-specific path first
            string platformName = GetPlatformName();
            string platformSpecificPath = Path.Combine(Application.streamingAssetsPath, "aa", platformName, VERSION_FILE_NAME);

            // Return platform-specific path (caller will check if file exists)
            return platformSpecificPath;
        }

        /// <summary>
        /// Gets all possible paths where version file might be located in StreamingAssets.
        /// Returns paths in order of priority.
        /// </summary>
        public static string[] GetStreamingAssetsVersionPaths()
        {
            var paths = new System.Collections.Generic.List<string>();

            // Priority 1: Platform-specific path (StreamingAssets/aa/<Platform>/AddressablesVersion.json)
            string platformName = GetPlatformName();
            paths.Add(Path.Combine(Application.streamingAssetsPath, "aa", platformName, VERSION_FILE_NAME));

            // Priority 2: Check other platform directories (in case of cross-platform builds)
            try
            {
                string addressablesRoot = Path.Combine(Application.streamingAssetsPath, "aa");
                if (Directory.Exists(addressablesRoot))
                {
                    string[] platformDirs = Directory.GetDirectories(addressablesRoot);
                    foreach (string platformDir in platformDirs)
                    {
                        string versionPath = Path.Combine(platformDir, VERSION_FILE_NAME);
                        if (!paths.Contains(versionPath))
                        {
                            paths.Add(versionPath);
                        }
                    }
                }
            }
            catch
            {
                // On some platforms (Android/WebGL), directory operations may not work
                // Continue with fallback path
            }

            // Priority 3: Root StreamingAssets (for backward compatibility)
            paths.Add(Path.Combine(Application.streamingAssetsPath, VERSION_FILE_NAME));

            return paths.ToArray();
        }

        private static string GetPlatformName()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString();
#elif UNITY_STANDALONE_WIN
            return "StandaloneWindows64";
#elif UNITY_STANDALONE_OSX
            return "StandaloneOSX";
#elif UNITY_STANDALONE_LINUX
            return "StandaloneLinux64";
#elif UNITY_ANDROID
            return "Android";
#elif UNITY_IOS
            return "iOS";
#elif UNITY_WEBGL
            return "WebGL";
#else
            return Application.platform.ToString();
#endif
        }

        /// <summary>
        /// Gets the Addressables cache root directory based on platform.
        /// Similar to YooAsset's path management.
        /// </summary>
        private static string GetAddressablesCacheRoot()
        {
#if UNITY_EDITOR
            // Editor: use project root
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectPath, ADDRESSABLES_CACHE_FOLDER);
#elif UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
            // Windows/Linux: use data path
            return Path.Combine(Application.dataPath, ADDRESSABLES_CACHE_FOLDER);
#elif UNITY_STANDALONE_OSX
            // Mac: use persistent data path
            return Path.Combine(Application.persistentDataPath, ADDRESSABLES_CACHE_FOLDER);
#else
            // Mobile platforms: use persistent data path (writable)
            return Path.Combine(Application.persistentDataPath, ADDRESSABLES_CACHE_FOLDER);
#endif
        }

        /// <summary>
        /// Constructs remote version URL from catalog URL.
        /// If catalog URL is "https://server.com/path/catalog.json",
        /// version URL will be "https://server.com/path/AddressablesVersion.json"
        /// </summary>
        public static string GetRemoteVersionUrl(string catalogUrl)
        {
            if (string.IsNullOrEmpty(catalogUrl))
                return string.Empty;

            try
            {
                if (!catalogUrl.StartsWith("http://") && !catalogUrl.StartsWith("https://"))
                    return string.Empty;

                System.Uri catalogUri = new System.Uri(catalogUrl);
                int lastSlashIndex = catalogUri.AbsolutePath.LastIndexOf('/');
                if (lastSlashIndex < 0)
                    return string.Empty;

                string directory = catalogUri.AbsolutePath.Substring(0, lastSlashIndex);
                return $"{catalogUri.Scheme}://{catalogUri.Authority}{directory}/{VERSION_FILE_NAME}";
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
#endif