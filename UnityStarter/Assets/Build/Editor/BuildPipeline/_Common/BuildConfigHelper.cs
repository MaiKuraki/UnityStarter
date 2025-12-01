using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Helper class for managing build configuration assets.
    /// Provides unified methods to get configuration assets with proper error handling
    /// for cases where multiple configs exist or no config exists.
    /// All configs are found using AssetDatabase.FindAssets by type name, no hardcoded paths.
    /// Includes caching to improve performance.
    /// </summary>
    public static class BuildConfigHelper
    {
        private static readonly Dictionary<Type, ScriptableObject> _configCache = new Dictionary<Type, ScriptableObject>();
        private static readonly Dictionary<Type, string> _configPathCache = new Dictionary<Type, string>();
        private static double _lastCacheRefreshTime = 0;
        private const double CacheRefreshInterval = 1.0; // Refresh cache every 1 second

        static BuildConfigHelper()
        {
            // Clear cache when assets are refreshed
            AssetDatabase.importPackageCompleted += (packageName) => ClearCache();
            AssetDatabase.importPackageCancelled += (packageName) => ClearCache();
        }

        /// <summary>
        /// Clears the configuration cache. Call this when assets are modified.
        /// </summary>
        public static void ClearCache()
        {
            _configCache.Clear();
            _configPathCache.Clear();
            _lastCacheRefreshTime = 0;
        }

        private static bool ShouldRefreshCache()
        {
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - _lastCacheRefreshTime > CacheRefreshInterval)
            {
                _lastCacheRefreshTime = currentTime;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets a single configuration asset of the specified type.
        /// Uses AssetDatabase.FindAssets to search by type name, no hardcoded paths.
        /// Results are cached for performance.
        /// </summary>
        /// <typeparam name="T">The configuration type.</typeparam>
        /// <param name="configTypeName">Display name for error messages (e.g., "BuildData", "YooAssetBuildConfig").</param>
        /// <returns>The configuration asset, or null if not found or multiple found.</returns>
        public static T GetConfig<T>(string configTypeName) where T : ScriptableObject
        {
            Type configType = typeof(T);

            // Check cache first
            if (_configCache.TryGetValue(configType, out var cachedConfig) && cachedConfig != null)
            {
                // Verify the cached asset still exists
                if (AssetDatabase.Contains(cachedConfig))
                {
                    return cachedConfig as T;
                }
                else
                {
                    // Asset was deleted, remove from cache
                    _configCache.Remove(configType);
                    _configPathCache.Remove(configType);
                }
            }

            // Refresh cache periodically or if not found
            if (ShouldRefreshCache() || !_configCache.ContainsKey(configType))
            {
                T config = FindConfigAsset<T>(configTypeName);
                if (config != null)
                {
                    _configCache[configType] = config;
                }
                return config;
            }

            return null;
        }

        private static T FindConfigAsset<T>(string configTypeName) where T : ScriptableObject
        {
            string typeName = typeof(T).Name;
            string[] guids = AssetDatabase.FindAssets($"t:{typeName}");

            if (guids.Length == 0)
            {
                Debug.LogError($"[BuildConfig] No {configTypeName} found! Please create a {configTypeName} asset in the project.");
                return null;
            }

            if (guids.Length > 1)
            {
                List<string> paths = new List<string>();
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    paths.Add(path);
                }

                Debug.LogError(
                    $"[BuildConfig] Found {guids.Length} {configTypeName} assets. " +
                    $"Only one {configTypeName} should exist in the project.\n" +
                    $"Found at:\n{string.Join("\n", paths.Select(p => $"  - {p}"))}\n" +
                    $"Using the first one found: {paths[0]}");
            }

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                T config = AssetDatabase.LoadAssetAtPath<T>(path);
                if (config != null)
                {
                    if (guids.Length == 1)
                    {
                        Debug.Log($"[BuildConfig] Loaded {configTypeName} from: {path}");
                    }
                    _configPathCache[typeof(T)] = path;
                    return config;
                }
            }

            Debug.LogError($"[BuildConfig] Failed to load {configTypeName} from found assets.");
            return null;
        }

        /// <summary>
        /// Gets BuildData configuration asset.
        /// </summary>
        public static BuildData GetBuildData()
        {
            return GetConfig<BuildData>("BuildData");
        }

        /// <summary>
        /// Gets YooAssetBuildConfig configuration asset.
        /// </summary>
        public static YooAssetBuildConfig GetYooAssetConfig()
        {
            return GetConfig<YooAssetBuildConfig>("YooAssetBuildConfig");
        }

        /// <summary>
        /// Gets AddressablesBuildConfig configuration asset.
        /// </summary>
        public static AddressablesBuildConfig GetAddressablesConfig()
        {
            return GetConfig<AddressablesBuildConfig>("AddressablesBuildConfig");
        }

        /// <summary>
        /// Gets HybridCLRBuildConfig configuration asset.
        /// </summary>
        public static HybridCLRBuildConfig GetHybridCLRConfig()
        {
            return GetConfig<HybridCLRBuildConfig>("HybridCLRBuildConfig");
        }
    }
}