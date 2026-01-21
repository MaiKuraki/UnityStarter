using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Factory implementation for creating Dynamic Atlas instances.
    /// Supports platform-specific configurations and shared instances.
    /// </summary>
    public class DynamicAtlasFactory : IDynamicAtlasFactory
    {
        private static IDynamicAtlas _sharedInstance;
        private static readonly object _sharedLock = new object();
        private static DynamicAtlasConfig _sharedConfig;

        /// <summary>
        /// Creates a new Dynamic Atlas service instance.
        /// </summary>
        public IDynamicAtlas Create(DynamicAtlasConfig config = null)
        {
            config = config ?? new DynamicAtlasConfig();

            // Validate config before creating
            if (!config.Validate(out string error))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"[DynamicAtlasFactory] Config validation warning: {error}. Using defaults.");
#endif
                config = new DynamicAtlasConfig();
            }

            return new DynamicAtlasService(config);
        }

        /// <summary>
        /// Creates a platform-optimized Dynamic Atlas service instance.
        /// </summary>
        /// <param name="loadFunc">Custom texture loader</param>
        /// <param name="unloadFunc">Custom texture unloader</param>
        /// <param name="useCompression">Whether to use compressed texture format</param>
        public IDynamicAtlas CreatePlatformOptimized(
            System.Func<string, Texture2D> loadFunc = null,
            System.Action<string, Texture2D> unloadFunc = null,
            bool useCompression = false)
        {
            var config = DynamicAtlasConfig.CreatePlatformOptimized(loadFunc, unloadFunc, useCompression);
            return Create(config);
        }

        /// <summary>
        /// Gets or creates a shared singleton instance.
        /// </summary>
        public IDynamicAtlas GetSharedInstance(DynamicAtlasConfig config = null)
        {
            if (_sharedInstance == null)
            {
                lock (_sharedLock)
                {
                    if (_sharedInstance == null)
                    {
                        _sharedInstance = Create(config);
                        _sharedConfig = config;
                    }
                }
            }
            else if (config != null && !ConfigEquals(_sharedConfig, config))
            {
                // If config changed, recreate the instance
                lock (_sharedLock)
                {
                    if (_sharedInstance != null && !ConfigEquals(_sharedConfig, config))
                    {
                        _sharedInstance.Dispose();
                        _sharedInstance = Create(config);
                        _sharedConfig = config;
                    }
                }
            }

            return _sharedInstance;
        }

        /// <summary>
        /// Clears the shared instance.
        /// </summary>
        public static void ClearSharedInstance()
        {
            lock (_sharedLock)
            {
                _sharedInstance?.Dispose();
                _sharedInstance = null;
                _sharedConfig = null;
            }
        }

        private static bool ConfigEquals(DynamicAtlasConfig a, DynamicAtlasConfig b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            return a.pageSize == b.pageSize &&
                   a.autoScaleLargeTextures == b.autoScaleLargeTextures &&
                   a.targetFormat == b.targetFormat &&
                   a.enableBlockAlignment == b.enableBlockAlignment &&
                   a.padding == b.padding &&
                   a.enablePlatformOptimizations == b.enablePlatformOptimizations &&
                   a.loadFunc == b.loadFunc &&
                   a.unloadFunc == b.unloadFunc;
        }
    }
}