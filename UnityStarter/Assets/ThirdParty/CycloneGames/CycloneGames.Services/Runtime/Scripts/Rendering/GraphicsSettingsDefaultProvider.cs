using UnityEngine;

namespace CycloneGames.Service.Runtime
{
    /// <summary>
    /// Provides platform-aware default graphics settings based on device capabilities.
    /// Automatically detects hardware tier on first launch.
    /// </summary>
    public class GraphicsSettingsDefaultProvider : IDefaultProvider<GraphicsSettingsData>
    {
        public const int CURRENT_SETTINGS_VERSION = 1;

        private const int FRAME_RATE_MOBILE = 60;
        private const int FRAME_RATE_CONSOLE = 60;
        private const int FRAME_RATE_DESKTOP = -1;  // Uncapped

        public GraphicsSettingsData GetDefault()
        {
            var tier = GetDeviceTier();
            return CreateSettingsForTier(tier);
        }

        private enum DeviceTier { Low, Medium, High, Ultra }

        private DeviceTier GetDeviceTier()
        {
#if UNITY_EDITOR
            return DeviceTier.Ultra;
#else
            int gpuMemory = SystemInfo.graphicsMemorySize;
            int processorCount = SystemInfo.processorCount;
            int systemMemory = SystemInfo.systemMemorySize;

            if (Application.isMobilePlatform)
            {
                if (gpuMemory >= 4096 && processorCount >= 6)
                    return DeviceTier.High;
                if (gpuMemory >= 2048 && processorCount >= 4)
                    return DeviceTier.Medium;
                return DeviceTier.Low;
            }

            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return DeviceTier.Low;

            if (IsConsolePlatform())
            {
                // Current-gen consoles (PS5, Xbox Series X) = High/Ultra
                // Last-gen (PS4, Xbox One, Switch) = Medium/High
                if (systemMemory >= 12288)
                    return DeviceTier.Ultra;
                if (systemMemory >= 8192)
                    return DeviceTier.High;
                return DeviceTier.Medium;
            }

            // Desktop
            if (gpuMemory >= 8192 && processorCount >= 8 && systemMemory >= 16384)
                return DeviceTier.Ultra;
            if (gpuMemory >= 4096 && processorCount >= 6 && systemMemory >= 8192)
                return DeviceTier.High;
            if (gpuMemory >= 2048 && processorCount >= 4 && systemMemory >= 4096)
                return DeviceTier.Medium;

            return DeviceTier.Low;
#endif
        }

        private GraphicsSettingsData CreateSettingsForTier(DeviceTier tier)
        {
            return tier switch
            {
                DeviceTier.Low => CreateLowSettings(),
                DeviceTier.Medium => CreateMediumSettings(),
                DeviceTier.High => CreateHighSettings(),
                DeviceTier.Ultra => CreateUltraSettings(),
                _ => CreateMediumSettings()
            };
        }

        private GraphicsSettingsData CreateLowSettings()
        {
            return new GraphicsSettingsData
            {
                SettingsVersion = CURRENT_SETTINGS_VERSION,
                QualityLevel = 0,
                TargetFrameRate = GetDefaultFrameRate(),
                VSyncCount = 0,
                AntiAliasingLevel = 0,
                ShadowDistance = 30f,
                TextureQuality = 2,         // Quarter resolution
                AnisotropicFiltering = 0,   // Disabled
                LodBias = 0.7f,
                SoftParticles = false,
                RenderScale = 0.75f,
                HDREnabled = false,
                ShortEdgeResolution = 540,
                FullScreenMode = GetDefaultFullScreenMode()
            };
        }

        private GraphicsSettingsData CreateMediumSettings()
        {
            return new GraphicsSettingsData
            {
                SettingsVersion = CURRENT_SETTINGS_VERSION,
                QualityLevel = 1,
                TargetFrameRate = GetDefaultFrameRate(),
                VSyncCount = Application.isMobilePlatform ? 0 : 1,
                AntiAliasingLevel = 2,
                ShadowDistance = 50f,
                TextureQuality = 1,         // Half resolution
                AnisotropicFiltering = 1,   // Per texture
                LodBias = 1.0f,
                SoftParticles = true,
                RenderScale = 1.0f,
                HDREnabled = false,
                ShortEdgeResolution = 720,
                FullScreenMode = GetDefaultFullScreenMode()
            };
        }

        private GraphicsSettingsData CreateHighSettings()
        {
            return new GraphicsSettingsData
            {
                SettingsVersion = CURRENT_SETTINGS_VERSION,
                QualityLevel = 2,
                TargetFrameRate = GetDefaultFrameRate(),
                VSyncCount = 1,
                AntiAliasingLevel = 4,
                ShadowDistance = 80f,
                TextureQuality = 0,         // Full resolution
                AnisotropicFiltering = 2,   // Force enable
                LodBias = 1.5f,
                SoftParticles = true,
                RenderScale = 1.0f,
                HDREnabled = true,
                ShortEdgeResolution = 1080,
                FullScreenMode = GetDefaultFullScreenMode()
            };
        }

        private GraphicsSettingsData CreateUltraSettings()
        {
            return new GraphicsSettingsData
            {
                SettingsVersion = CURRENT_SETTINGS_VERSION,
                QualityLevel = QualitySettings.names.Length - 1,
                TargetFrameRate = -1,       // Uncapped
                VSyncCount = 1,
                AntiAliasingLevel = 8,
                ShadowDistance = 150f,
                TextureQuality = 0,         // Full resolution
                AnisotropicFiltering = 2,   // Force enable
                LodBias = 2.0f,
                SoftParticles = true,
                RenderScale = 1.0f,
                HDREnabled = true,
                ShortEdgeResolution = 2160,  // 4K
                FullScreenMode = GetDefaultFullScreenMode()
            };
        }

        private static int GetDefaultFrameRate()
        {
            if (Application.isMobilePlatform)
                return FRAME_RATE_MOBILE;
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return FRAME_RATE_MOBILE;
            if (IsConsolePlatform())
                return FRAME_RATE_CONSOLE;
            return FRAME_RATE_DESKTOP;
        }

        // 0=ExclusiveFullScreen (desktop default), 1=FullScreenWindow (borderless)
        // Mobile/console/WebGL always use native fullscreen
        private static int GetDefaultFullScreenMode()
        {
            if (Application.isMobilePlatform || IsConsolePlatform() ||
                Application.platform == RuntimePlatform.WebGLPlayer)
                return 0; // ExclusiveFullScreen
            return 1; // FullScreenWindow (borderless — preferred for modern desktop)
        }

        private static bool IsConsolePlatform()
        {
            var p = Application.platform;
            return p == RuntimePlatform.PS4 || p == RuntimePlatform.PS5 ||
                   p == RuntimePlatform.XboxOne ||
                   p == RuntimePlatform.GameCoreXboxOne || p == RuntimePlatform.GameCoreXboxSeries ||
                   p == RuntimePlatform.Switch;
        }
    }
}