#if CYCLONEGAMES_HAS_ADDRESSABLES
using System;
using System.IO;

using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Resolves product-owned Addressables version metadata paths without inspecting provider internals.
    /// </summary>
    internal static class AddressablesVersionPathHelper
    {
        private const string VERSION_FILE_NAME = "AddressablesVersion.json";
        private const string ADDRESSABLES_CACHE_FOLDER = "com.unity.addressables";

        public static string GetPersistentVersionPath()
        {
            return Path.Combine(
                Application.persistentDataPath,
                ADDRESSABLES_CACHE_FOLDER,
                VERSION_FILE_NAME);
        }

        public static string GetStreamingAssetsVersionPath()
        {
            string platformFolder = GetRuntimePlatformFolder();
            return string.IsNullOrEmpty(platformFolder)
                ? string.Empty
                : Path.Combine(
                    Application.streamingAssetsPath,
                    "aa",
                    platformFolder,
                    VERSION_FILE_NAME);
        }

        public static string[] GetStreamingAssetsVersionPaths()
        {
            string path = GetStreamingAssetsVersionPath();
            return string.IsNullOrEmpty(path)
                ? Array.Empty<string>()
                : new[] { path };
        }

        private static string GetRuntimePlatformFolder()
        {
#if UNITY_EDITOR
            // Active build-target mapping belongs to build tooling, not a runtime assembly.
            return string.Empty;
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
            // Console and future platform folders must be supplied by explicit build metadata.
            return string.Empty;
#endif
        }
    }
}
#endif
