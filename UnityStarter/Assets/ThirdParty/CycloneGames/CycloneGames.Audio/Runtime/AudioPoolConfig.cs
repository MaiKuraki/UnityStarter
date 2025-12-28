// Copyright (c) CycloneGames
// Licensed under the MIT License.

using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Configuration for the smart audio pool system.
    /// Allows customization of pool sizes per platform and device tier.
    /// </summary>
    [CreateAssetMenu(menuName = "CycloneGames/Audio/Audio Pool Config")]
    public class AudioPoolConfig : ScriptableObject
    {
        [Header("WebGL Platform")]
        [Tooltip("Maximum pool size for WebGL builds (browser limitations)")]
        [Range(8, 64)]
        [SerializeField] private int webGLMaxPoolSize = 32;

        [Header("Mobile Platforms (Android/iOS)")]
        [Tooltip("Max pool size for low-end devices (RAM < 3GB)")]
        [Range(16, 96)]
        [SerializeField] private int mobileLowEndMaxPoolSize = 48;

        [Tooltip("Max pool size for mid-range devices (RAM 3-6GB)")]
        [Range(32, 128)]
        [SerializeField] private int mobileMidRangeMaxPoolSize = 64;

        [Tooltip("Max pool size for high-end devices (RAM > 6GB)")]
        [Range(48, 192)]
        [SerializeField] private int mobileHighEndMaxPoolSize = 96;

        [Header("Desktop Platforms")]
        [Tooltip("Max pool size for low-end desktops (RAM < 8GB)")]
        [Range(64, 256)]
        [SerializeField] private int desktopLowEndMaxPoolSize = 128;

        [Tooltip("Max pool size for mid-range desktops (RAM 8-16GB)")]
        [Range(96, 384)]
        [SerializeField] private int desktopMidRangeMaxPoolSize = 192;

        [Tooltip("Max pool size for high-end desktops (RAM > 16GB)")]
        [Range(128, 512)]
        [SerializeField] private int desktopHighEndMaxPoolSize = 256;

        [Header("Initial Pool Sizes")]
        [Tooltip("Initial pool size for WebGL")]
        [Range(8, 32)]
        [SerializeField] private int webGLInitialPoolSize = 16;

        [Tooltip("Initial pool size for mobile platforms")]
        [Range(16, 64)]
        [SerializeField] private int mobileInitialPoolSize = 32;

        [Tooltip("Initial pool size for desktop platforms")]
        [Range(32, 128)]
        [SerializeField] private int desktopInitialPoolSize = 80;

        [Header("Pool Expansion")]
        [Tooltip("Number of sources to add when expanding the pool")]
        [Range(4, 32)]
        [SerializeField] private int expansionIncrement = 8;

        [Header("Pool Shrinking")]
        [Tooltip("Seconds of idle time before pool starts shrinking")]
        [Range(5f, 60f)]
        [SerializeField] private float shrinkIdleThreshold = 10f;

        [Tooltip("Pool usage ratio below which shrinking can occur (0.0 - 1.0)")]
        [Range(0.1f, 0.8f)]
        [SerializeField] private float shrinkUsageThreshold = 0.5f;

        [Tooltip("Minimum seconds between shrink operations")]
        [Range(0.5f, 5f)]
        [SerializeField] private float shrinkInterval = 1f;

        // Public accessors
        public int WebGLMaxPoolSize => webGLMaxPoolSize;
        public int MobileLowEndMaxPoolSize => mobileLowEndMaxPoolSize;
        public int MobileMidRangeMaxPoolSize => mobileMidRangeMaxPoolSize;
        public int MobileHighEndMaxPoolSize => mobileHighEndMaxPoolSize;
        public int DesktopLowEndMaxPoolSize => desktopLowEndMaxPoolSize;
        public int DesktopMidRangeMaxPoolSize => desktopMidRangeMaxPoolSize;
        public int DesktopHighEndMaxPoolSize => desktopHighEndMaxPoolSize;

        public int WebGLInitialPoolSize => webGLInitialPoolSize;
        public int MobileInitialPoolSize => mobileInitialPoolSize;
        public int DesktopInitialPoolSize => desktopInitialPoolSize;

        public int ExpansionIncrement => expansionIncrement;
        public float ShrinkIdleThreshold => shrinkIdleThreshold;
        public float ShrinkUsageThreshold => shrinkUsageThreshold;
        public float ShrinkInterval => shrinkInterval;

        /// <summary>
        /// Get the maximum pool size for the current device based on platform and RAM.
        /// </summary>
        public int GetMaxPoolSizeForDevice()
        {
            int ramMB = SystemInfo.systemMemorySize;

#if UNITY_WEBGL
            return webGLMaxPoolSize;
#elif UNITY_ANDROID || UNITY_IOS
            if (ramMB < 3072) return mobileLowEndMaxPoolSize;      // < 3 GB
            if (ramMB < 6144) return mobileMidRangeMaxPoolSize;    // 3-6 GB
            return mobileHighEndMaxPoolSize;                        // > 6 GB
#else
            if (ramMB < 8192) return desktopLowEndMaxPoolSize;     // < 8 GB
            if (ramMB < 16384) return desktopMidRangeMaxPoolSize;  // 8-16 GB
            return desktopHighEndMaxPoolSize;                       // > 16 GB
#endif
        }

        /// <summary>
        /// Get the initial pool size for the current platform.
        /// </summary>
        public int GetInitialPoolSizeForPlatform()
        {
#if UNITY_WEBGL
            return webGLInitialPoolSize;
#elif UNITY_ANDROID || UNITY_IOS
            return mobileInitialPoolSize;
#else
            return desktopInitialPoolSize;
#endif
        }

        /// <summary>
        /// Get the device tier name for debugging purposes.
        /// </summary>
        public string GetDeviceTierName()
        {
            int ramMB = SystemInfo.systemMemorySize;

#if UNITY_WEBGL
            return "WebGL";
#elif UNITY_ANDROID || UNITY_IOS
            if (ramMB < 3072) return "Mobile Low-End";
            if (ramMB < 6144) return "Mobile Mid-Range";
            return "Mobile High-End";
#else
            if (ramMB < 8192) return "Desktop Low-End";
            if (ramMB < 16384) return "Desktop Mid-Range";
            return "Desktop High-End";
#endif
        }

        #region Static Defaults (used when no config exists)

        /// <summary>
        /// Get default max pool size for device when no config asset exists.
        /// </summary>
        public static int GetDefaultMaxPoolSizeForDevice()
        {
            int ramMB = SystemInfo.systemMemorySize;

#if UNITY_WEBGL
            return 32;
#elif UNITY_ANDROID || UNITY_IOS
            if (ramMB < 3072) return 48;
            if (ramMB < 6144) return 64;
            return 96;
#else
            if (ramMB < 8192) return 128;
            if (ramMB < 16384) return 192;
            return 256;
#endif
        }

        /// <summary>
        /// Get default initial pool size for platform when no config asset exists.
        /// </summary>
        public static int GetDefaultInitialPoolSizeForPlatform()
        {
#if UNITY_WEBGL
            return 16;
#elif UNITY_ANDROID || UNITY_IOS
            return 32;
#else
            return 80;
#endif
        }

        /// <summary>
        /// Get default device tier name.
        /// </summary>
        public static string GetDefaultDeviceTierName()
        {
            int ramMB = SystemInfo.systemMemorySize;

#if UNITY_WEBGL
            return "WebGL";
#elif UNITY_ANDROID || UNITY_IOS
            if (ramMB < 3072) return "Mobile Low-End";
            if (ramMB < 6144) return "Mobile Mid-Range";
            return "Mobile High-End";
#else
            if (ramMB < 8192) return "Desktop Low-End";
            if (ramMB < 16384) return "Desktop Mid-Range";
            return "Desktop High-End";
#endif
        }

        public const int DefaultExpansionIncrement = 8;
        public const float DefaultShrinkIdleThreshold = 10f;
        public const float DefaultShrinkUsageThreshold = 0.5f;
        public const float DefaultShrinkInterval = 1f;

        #endregion

        #region Auto-Discovery

        private static AudioPoolConfig cachedConfig;
        private static bool hasSearchedForConfig;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void ResetCacheOnDomainReload()
        {
            ClearCache();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCacheOnPlayModeEnter()
        {
            ClearCache();
        }

        /// <summary>
        /// Find the AudioPoolConfig in the project. Returns null if none exists.
        /// Caches the result after first search.
        /// </summary>
        public static AudioPoolConfig FindConfig()
        {
            if (hasSearchedForConfig && cachedConfig != null) return cachedConfig;
            
            // Reset search flag if config was destroyed
            if (hasSearchedForConfig && cachedConfig == null)
            {
                hasSearchedForConfig = false;
            }
            
            hasSearchedForConfig = true;

            // Try to find in Resources first (multiple possible names)
            cachedConfig = Resources.Load<AudioPoolConfig>("AudioPoolConfig");
            if (cachedConfig != null) return cachedConfig;
            
            cachedConfig = Resources.Load<AudioPoolConfig>("Audio Pool Config");
            if (cachedConfig != null) return cachedConfig;

            // Try to find any AudioPoolConfig in Resources
            var allConfigs = Resources.LoadAll<AudioPoolConfig>("");
            if (allConfigs != null && allConfigs.Length > 0)
            {
                cachedConfig = allConfigs[0];
                if (allConfigs.Length > 1)
                {
                    Debug.LogWarning($"AudioPoolConfig: Found {allConfigs.Length} configs in Resources. Using first.");
                }
                return cachedConfig;
            }

            // Fallback: find all instances using AssetDatabase (Editor only)
#if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets("t:AudioPoolConfig");
            if (guids.Length > 0)
            {
                if (guids.Length > 1)
                {
                    Debug.LogWarning($"AudioPoolConfig: Found {guids.Length} configs in project. Only one should exist. Using first found.");
                }
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                cachedConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioPoolConfig>(path);
            }
#endif
            return cachedConfig;
        }

        /// <summary>
        /// Manually set the config from an external source (e.g., YooAsset, Addressables).
        /// Call this before AudioManager initializes to use a hot-updated config.
        /// </summary>
        /// <param name="config">The config loaded from your asset management system.</param>
        public static void SetConfig(AudioPoolConfig config)
        {
            cachedConfig = config;
            hasSearchedForConfig = true;
            
            if (config != null)
            {
                Debug.Log($"AudioPoolConfig: External config set (Device tier: {config.GetDeviceTierName()})");
            }
        }

        /// <summary>
        /// Clear the cached config (useful when config is deleted/changed).
        /// </summary>
        public static void ClearCache()
        {
            cachedConfig = null;
            hasSearchedForConfig = false;
        }

        #endregion
    }
}

