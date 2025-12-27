using VYaml.Annotations;

namespace CycloneGames.Service.Runtime
{
    /// <summary>
    /// Upscaler technology selection. Availability depends on platform and GPU.
    /// </summary>
    public enum UpscalerTechnology
    {
        None = 0,           // Native rendering with MSAA
        FSR3 = 1,           // AMD FSR 3.x (temporal + frame gen)
        SGSR2 = 2,          // Qualcomm SGSR 2 (mobile optimized)
        // Future expansion
        DLSS = 10,          // NVIDIA DLSS (requires RTX)
        XeSS = 11,          // Intel XeSS
    }

    /// <summary>
    /// Quality preset mapping to scale factor. Lower number = higher quality.
    /// </summary>
    public enum UpscalerQualityPreset
    {
        Off = -1,               // Upscaler disabled
        NativeAA = 0,           // 1.0x - Anti-aliasing only, no upscaling
        UltraQuality = 1,       // 1.2x - Near-native quality
        Quality = 2,            // 1.5x - Recommended for high-end
        Balanced = 3,           // 1.7x - Balance of quality and performance
        Performance = 4,        // 2.0x - Significant performance boost
        UltraPerformance = 5,   // 3.0x - Maximum performance, lower quality
    }

    /// <summary>
    /// Persistent upscaler settings. Separate from graphics settings for modularity.
    /// Uses conditional compilation for FSR3/SGSR2/DLSS support.
    /// </summary>
    [YamlObject]
    public partial struct UpscalerSettingsData
    {
        // Which upscaler technology to use. Check availability at runtime.
        public UpscalerTechnology Technology;

        // Quality preset. Maps to internal scale factors.
        public UpscalerQualityPreset QualityPreset;

        // Custom scale factor (0.33-1.0). Used only when QualityPreset is not a standard preset.
        public float CustomScaleFactor;

        // Post-upscale sharpening. Improves clarity at lower resolutions.
        public bool SharpeningEnabled;

        // Sharpness strength (0.0-1.0). Higher = sharper but may introduce artifacts.
        public float SharpnessStrength;

        // Frame generation (FSR3 only). Doubles apparent frame rate.
        public bool FrameGenerationEnabled;

        /// <summary>
        /// Get the internal scale factor for a quality preset.
        /// Returns render resolution / display resolution ratio.
        /// </summary>
        public static float GetScaleFactorForPreset(UpscalerQualityPreset preset)
        {
            return preset switch
            {
                UpscalerQualityPreset.Off => 1.0f,
                UpscalerQualityPreset.NativeAA => 1.0f,
                UpscalerQualityPreset.UltraQuality => 1.0f / 1.2f,   // ~0.83
                UpscalerQualityPreset.Quality => 1.0f / 1.5f,        // ~0.67
                UpscalerQualityPreset.Balanced => 1.0f / 1.7f,       // ~0.59
                UpscalerQualityPreset.Performance => 0.5f,           // 0.50
                UpscalerQualityPreset.UltraPerformance => 1.0f / 3f, // ~0.33
                _ => 1.0f
            };
        }
    }
}