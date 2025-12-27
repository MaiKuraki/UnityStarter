using UnityEngine;

namespace CycloneGames.Service.Runtime
{
    /// <summary>
    /// Provides platform-aware default graphics settings based on device capabilities.
    /// Automatically detects hardware tier on first launch.
    /// </summary>
    public class GraphicsSettingsDefaultProvider : IDefaultProvider<GraphicsSettingsData>
    {
        private const int FRAME_RATE_MOBILE = 60;
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

            // Desktop/Console
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
                ShortEdgeResolution = 540
            };
        }

        private GraphicsSettingsData CreateMediumSettings()
        {
            return new GraphicsSettingsData
            {
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
                ShortEdgeResolution = 720
            };
        }

        private GraphicsSettingsData CreateHighSettings()
        {
            return new GraphicsSettingsData
            {
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
                ShortEdgeResolution = 1080
            };
        }

        private GraphicsSettingsData CreateUltraSettings()
        {
            return new GraphicsSettingsData
            {
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
                ShortEdgeResolution = 2160   // 4K
            };
        }

        private int GetDefaultFrameRate()
        {
            if (Application.isMobilePlatform)
                return FRAME_RATE_MOBILE;
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return FRAME_RATE_MOBILE;
            return FRAME_RATE_DESKTOP;
        }
    }
}