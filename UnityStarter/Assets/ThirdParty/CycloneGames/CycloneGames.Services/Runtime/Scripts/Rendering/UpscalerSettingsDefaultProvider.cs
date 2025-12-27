using UnityEngine;
using UnityEngine.Rendering;
using CycloneGames.Logger;

namespace CycloneGames.Service.Runtime
{
    /// <summary>
    /// Provides platform-aware default upscaler settings with intelligent technology detection.
    /// Automatically selects the best upscaler based on platform, GPU vendor, and graphics API.
    /// </summary>
    public class UpscalerSettingsDefaultProvider : IDefaultProvider<UpscalerSettingsData>
    {
        private const string DEBUG_FLAG = "[UpscalerDetection]";

        public UpscalerSettingsData GetDefault()
        {
            // Log device information
            LogDeviceInfo();

            var (technology, quality) = DetectOptimalUpscaler();

            // Log selected strategy
            LogSelectedStrategy(technology, quality);

            return new UpscalerSettingsData
            {
                Technology = technology,
                QualityPreset = quality,
                CustomScaleFactor = 0.67f,
                SharpeningEnabled = true,
                SharpnessStrength = 0.5f,
                FrameGenerationEnabled = false,
            };
        }

        private void LogDeviceInfo()
        {
            var platform = Application.platform;
            var graphicsAPI = SystemInfo.graphicsDeviceType;
            string gpuName = SystemInfo.graphicsDeviceName;
            int gpuMemory = SystemInfo.graphicsMemorySize;

            CLogger.LogInfo($"{DEBUG_FLAG} Device: {platform} | {graphicsAPI} | {gpuName} ({gpuMemory}MB) | Upscalers: FSR3={IsFSR3Available()}, SGSR2={IsSGSR2Available()}");
        }

        private void LogSelectedStrategy(UpscalerTechnology technology, UpscalerQualityPreset quality)
        {
            if (technology == UpscalerTechnology.None)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Strategy: Disabled (native rendering)");
            }
            else
            {
                float scaleFactor = UpscalerSettingsData.GetScaleFactorForPreset(quality);
                CLogger.LogInfo($"{DEBUG_FLAG} Strategy: {technology} @ {quality} ({scaleFactor:F2}x)");
            }
        }

        /// <summary>
        /// Detect the optimal upscaler technology based on platform, GPU, and graphics API.
        /// Returns (Technology, QualityPreset) tuple.
        /// </summary>
        private (UpscalerTechnology, UpscalerQualityPreset) DetectOptimalUpscaler()
        {
            var platform = Application.platform;
            var graphicsAPI = SystemInfo.graphicsDeviceType;
            string gpuName = SystemInfo.graphicsDeviceName.ToLowerInvariant();
            int gpuMemory = SystemInfo.graphicsMemorySize;

            // Mobile platforms: prefer SGSR2 (Qualcomm optimized)
            if (Application.isMobilePlatform)
            {
                if (IsSGSR2Available())
                    return (UpscalerTechnology.SGSR2, GetMobileQualityPreset(gpuMemory));
                return (UpscalerTechnology.None, UpscalerQualityPreset.Off);
            }

            // WebGL: no upscaler support
            if (platform == RuntimePlatform.WebGLPlayer)
                return (UpscalerTechnology.None, UpscalerQualityPreset.Off);

            // macOS: FSR3 not supported (Metal only, no DX12/Vulkan)
            if (platform == RuntimePlatform.OSXPlayer || platform == RuntimePlatform.OSXEditor)
            {
                // SGSR2 might work on Metal - check availability
                if (IsSGSR2Available())
                    return (UpscalerTechnology.SGSR2, UpscalerQualityPreset.Quality);
                return (UpscalerTechnology.None, UpscalerQualityPreset.Off);
            }

            // Linux: only Vulkan supports FSR3
            if (platform == RuntimePlatform.LinuxPlayer || platform == RuntimePlatform.LinuxEditor)
            {
                if (graphicsAPI == GraphicsDeviceType.Vulkan && IsFSR3Available())
                    return (UpscalerTechnology.FSR3, GetDesktopQualityPreset(gpuMemory));
                if (IsSGSR2Available())
                    return (UpscalerTechnology.SGSR2, UpscalerQualityPreset.Quality);
                return (UpscalerTechnology.None, UpscalerQualityPreset.Off);
            }

            // Windows: best support
            if (platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.WindowsEditor)
            {
                // Check DX12 or Vulkan (required for FSR3)
                bool supportsFSR3API = graphicsAPI == GraphicsDeviceType.Direct3D12 ||
                                        graphicsAPI == GraphicsDeviceType.Vulkan;

                // Integrated GPU: lower performance, consider disabling or using NativeAA
                if (IsIntegratedGPU(gpuName, gpuMemory))
                {
                    if (supportsFSR3API && IsFSR3Available())
                        return (UpscalerTechnology.FSR3, UpscalerQualityPreset.NativeAA);
                    return (UpscalerTechnology.None, UpscalerQualityPreset.Off);
                }

                // NVIDIA RTX: prefer DLSS (future), fallback to FSR3
                if (IsNvidiaRTX(gpuName))
                {
                    if (IsDLSSAvailable())
                        return (UpscalerTechnology.DLSS, UpscalerQualityPreset.Quality);
                    if (supportsFSR3API && IsFSR3Available())
                        return (UpscalerTechnology.FSR3, UpscalerQualityPreset.Quality);
                }

                // NVIDIA GTX: FSR3
                if (IsNvidiaGPU(gpuName))
                {
                    if (supportsFSR3API && IsFSR3Available())
                        return (UpscalerTechnology.FSR3, GetDesktopQualityPreset(gpuMemory));
                }

                // AMD: FSR3 native
                if (IsAmdGPU(gpuName))
                {
                    if (supportsFSR3API && IsFSR3Available())
                        return (UpscalerTechnology.FSR3, GetDesktopQualityPreset(gpuMemory));
                }

                // Intel Arc: FSR3 or XeSS
                if (IsIntelArc(gpuName))
                {
                    if (IsXeSSAvailable())
                        return (UpscalerTechnology.XeSS, UpscalerQualityPreset.Quality);
                    if (supportsFSR3API && IsFSR3Available())
                        return (UpscalerTechnology.FSR3, UpscalerQualityPreset.Balanced);
                }

                // Generic fallback: FSR3 if available
                if (supportsFSR3API && IsFSR3Available())
                    return (UpscalerTechnology.FSR3, UpscalerQualityPreset.Balanced);
            }

            // Console platforms (PS5, Xbox): FSR2/FSR3 if available
            if (IsFSR3Available())
                return (UpscalerTechnology.FSR3, UpscalerQualityPreset.Balanced);

            return (UpscalerTechnology.None, UpscalerQualityPreset.Off);
        }

        #region GPU Detection Helpers

        private static bool IsIntegratedGPU(string gpuName, int gpuMemory)
        {
            // Integrated GPUs typically have < 2GB dedicated VRAM
            if (gpuMemory < 2048) return true;

            // Known integrated GPU name patterns
            return gpuName.Contains("intel hd") ||
                   gpuName.Contains("intel uhd") ||
                   gpuName.Contains("intel iris") ||
                   gpuName.Contains("amd radeon graphics") ||    // AMD APU
                   gpuName.Contains("vega 8") ||
                   gpuName.Contains("vega 11");
        }

        private static bool IsNvidiaRTX(string gpuName)
        {
            return gpuName.Contains("rtx");
        }

        private static bool IsNvidiaGPU(string gpuName)
        {
            return gpuName.Contains("nvidia") ||
                   gpuName.Contains("geforce") ||
                   gpuName.Contains("gtx") ||
                   gpuName.Contains("rtx");
        }

        private static bool IsAmdGPU(string gpuName)
        {
            return gpuName.Contains("amd") ||
                   gpuName.Contains("radeon") ||
                   gpuName.Contains("rx ");
        }

        private static bool IsIntelArc(string gpuName)
        {
            return gpuName.Contains("intel arc") || gpuName.Contains("a770") || gpuName.Contains("a750");
        }

        #endregion

        #region Quality Preset Helpers

        private static UpscalerQualityPreset GetDesktopQualityPreset(int gpuMemory)
        {
            // Higher VRAM = can afford higher quality
            if (gpuMemory >= 8192) return UpscalerQualityPreset.UltraQuality;
            if (gpuMemory >= 6144) return UpscalerQualityPreset.Quality;
            if (gpuMemory >= 4096) return UpscalerQualityPreset.Balanced;
            return UpscalerQualityPreset.Performance;
        }

        private static UpscalerQualityPreset GetMobileQualityPreset(int gpuMemory)
        {
            if (gpuMemory >= 4096) return UpscalerQualityPreset.Quality;
            if (gpuMemory >= 2048) return UpscalerQualityPreset.Balanced;
            return UpscalerQualityPreset.Performance;
        }

        #endregion

        #region Availability Checks

        public static bool IsFSR3Available()
        {
#if FSR_3_PRESENT
            return true;
#else
            return false;
#endif
        }

        public static bool IsSGSR2Available()
        {
#if SGSR_2_PRESENT
            return true;
#else
            return false;
#endif
        }

        public static bool IsDLSSAvailable()
        {
#if DLSS_PRESENT
            return true;
#else
            return false;
#endif
        }

        public static bool IsXeSSAvailable()
        {
#if XESS_PRESENT
            return true;
#else
            return false;
#endif
        }

        public static UpscalerTechnology[] GetAvailableTechnologies()
        {
            var list = new System.Collections.Generic.List<UpscalerTechnology> { UpscalerTechnology.None };

            if (IsFSR3Available()) list.Add(UpscalerTechnology.FSR3);
            if (IsSGSR2Available()) list.Add(UpscalerTechnology.SGSR2);
            if (IsDLSSAvailable()) list.Add(UpscalerTechnology.DLSS);
            if (IsXeSSAvailable()) list.Add(UpscalerTechnology.XeSS);

            return list.ToArray();
        }

        #endregion
    }
}