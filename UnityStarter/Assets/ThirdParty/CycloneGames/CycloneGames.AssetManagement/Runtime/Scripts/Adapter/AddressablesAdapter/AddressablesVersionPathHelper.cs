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
        /// Addressables stores content in StreamingAssets/aa/<Platform> structure.
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
        /// 
        /// Priority order:
        /// - Bundle directory (StreamingAssets/aa/<BuildTarget>/AddressablesVersion.json) - where bundle files are located (correct location)
        /// - Other platform directories (in case of cross-platform builds)
        /// - Root StreamingAssets (for backward compatibility)
        /// </summary>
        public static string[] GetStreamingAssetsVersionPaths()
        {
            var paths = new System.Collections.Generic.List<string>();

            string buildTargetName = GetBuildTargetName();
            string platformName = GetPlatformName();
            
            // Priority 1: Bundle directory (correct location where bundle files are located)
            // This is StreamingAssets/aa/<BuildTarget>/AddressablesVersion.json
            // Unity copies BuildPath contents to StreamingAssets/aa, so the BuildTarget subdirectory
            // becomes StreamingAssets/aa/<BuildTarget>
            if (!string.IsNullOrEmpty(buildTargetName))
            {
                string bundleDirPath = Path.Combine(Application.streamingAssetsPath, "aa", buildTargetName, VERSION_FILE_NAME);
                paths.Add(bundleDirPath);
            }

            // StreamingAssets/aa/<Platform>/AddressablesVersion.json
            paths.Add(Path.Combine(Application.streamingAssetsPath, "aa", platformName, VERSION_FILE_NAME));

            // Check other platform directories and their subdirectories (in case of cross-platform builds)
            try
            {
                string addressablesRoot = Path.Combine(Application.streamingAssetsPath, "aa");
                if (Directory.Exists(addressablesRoot))
                {
                    string[] platformDirs = Directory.GetDirectories(addressablesRoot);
                    foreach (string platformDir in platformDirs)
                    {
                        // Check subdirectories (bundle directories) first
                        try
                        {
                            string[] subdirs = Directory.GetDirectories(platformDir);
                            foreach (string subdir in subdirs)
                            {
                                string versionPath = Path.Combine(subdir, VERSION_FILE_NAME);
                                if (!paths.Contains(versionPath))
                                {
                                    paths.Add(versionPath);
                                }
                            }
                        }
                        catch
                        {

                        }
                    }
                }
            }
            catch
            {
                // On some platforms (Android/WebGL), directory operations may not work
                // Continue with fallback path
            }

            paths.Add(Path.Combine(Application.streamingAssetsPath, VERSION_FILE_NAME));

            return paths.ToArray();
        }

        /// <summary>
        /// Gets the BuildTarget name at runtime (e.g., "StandaloneWindows64", "Android", "iOS").
        /// This is used to locate the bundle directory where version file is stored.
        /// </summary>
        private static string GetBuildTargetName()
        {
#if UNITY_EDITOR
            var buildTarget = UnityEditor.EditorUserBuildSettings.activeBuildTarget;
            return buildTarget.ToString();
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
            // For unknown platforms, try to map RuntimePlatform to BuildTarget name
            var runtimePlatform = Application.platform;
            switch (runtimePlatform)
            {
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                    return "StandaloneWindows64";
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor:
                    return "StandaloneOSX";
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                    return "StandaloneLinux64";
                case RuntimePlatform.Android:
                    return "Android";
                case RuntimePlatform.IPhonePlayer:
                    return "iOS";
                case RuntimePlatform.WebGLPlayer:
                    return "WebGL";
                default:
                    return runtimePlatform.ToString();
            }
#endif
        }

        private static string GetPlatformName()
        {
            // Use Unity Addressables' PlatformMappingService to get the correct platform path
            // This ensures consistency between build-time and runtime paths
            // For example: StandaloneWindows64 -> "Windows", StandaloneOSX -> "OSX"
#if ADDRESSABLES_PRESENT
            try
            {
                // Use reflection to call PlatformMappingService.GetPlatformPathSubFolder()
                // This matches what Addressables.BuildPath uses internally
                var platformMappingType = System.Type.GetType("UnityEngine.AddressableAssets.PlatformMappingService, Unity.Addressables");
                if (platformMappingType != null)
                {
                    var method = platformMappingType.GetMethod("GetPlatformPathSubFolder", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (method != null)
                    {
                        string platformPath = method.Invoke(null, null)?.ToString();
                        if (!string.IsNullOrEmpty(platformPath))
                        {
                            return platformPath;
                        }
                    }
                }
            }
            catch
            {
                // Fall through to hardcoded mapping if reflection fails
            }
#endif

            // Fallback to hardcoded mapping (matches Unity's PlatformMappingService logic)
#if UNITY_EDITOR
            var buildTarget = UnityEditor.EditorUserBuildSettings.activeBuildTarget;
            switch (buildTarget)
            {
                case UnityEditor.BuildTarget.StandaloneWindows:
                case UnityEditor.BuildTarget.StandaloneWindows64:
                    return "Windows";
                case UnityEditor.BuildTarget.StandaloneOSX:
                    return "OSX";
                case UnityEditor.BuildTarget.StandaloneLinux64:
                    return "Linux";
                case UnityEditor.BuildTarget.Android:
                    return "Android";
                case UnityEditor.BuildTarget.iOS:
                    return "iOS";
                case UnityEditor.BuildTarget.WebGL:
                    return "WebGL";
                default:
                    return buildTarget.ToString();
            }
#elif UNITY_STANDALONE_WIN
            return "Windows";
#elif UNITY_STANDALONE_OSX
            return "OSX";
#elif UNITY_STANDALONE_LINUX
            return "Linux";
#elif UNITY_ANDROID
            return "Android";
#elif UNITY_IOS
            return "iOS";
#elif UNITY_WEBGL
            return "WebGL";
#else
            // For unknown platforms, try to map RuntimePlatform to AddressablesPlatform
            var runtimePlatform = Application.platform;
            switch (runtimePlatform)
            {
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                    return "Windows";
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor:
                    return "OSX";
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                    return "Linux";
                case RuntimePlatform.Android:
                    return "Android";
                case RuntimePlatform.IPhonePlayer:
                    return "iOS";
                case RuntimePlatform.WebGLPlayer:
                    return "WebGL";
                default:
                    return runtimePlatform.ToString();
            }
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