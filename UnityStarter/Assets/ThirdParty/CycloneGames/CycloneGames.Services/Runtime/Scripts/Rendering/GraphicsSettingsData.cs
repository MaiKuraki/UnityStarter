using VYaml.Annotations;

namespace CycloneGames.Service.Runtime
{
    /// <summary>
    /// Persistent graphics settings data. Zero-GC struct optimized for VYaml serialization.
    /// Shadow resolution/cascades are configured via URP Asset or Quality Levels, not runtime.
    /// </summary>
    [YamlObject]
    public partial struct GraphicsSettingsData
    {
        // Higher = better quality. Maps to Unity's Quality Settings index.
        public int QualityLevel;

        // Target FPS. -1 = uncapped (platform default).
        public int TargetFrameRate;

        // 0=Off (may tear), 1=Sync every frame (recommended), 2+=Sync every N frames (rarely used).
        public int VSyncCount;

        // 0=None, 2=2x, 4=4x, 8=8x MSAA. Higher = better edge smoothing, more GPU cost.
        public int AntiAliasingLevel;

        // Shadow draw distance in meters. Higher = shadows visible farther, more GPU cost.
        public float ShadowDistance;

        // 0=Full, 1=Half, 2=Quarter, 3=Eighth. LOWER = better quality (0 is best).
        public int TextureQuality;

        // 0=Disable, 1=PerTexture, 2=ForceEnable. Higher = sharper textures at angles, more GPU cost.
        public int AnisotropicFiltering;

        // Range 0.3-2.0. Higher = objects stay detailed at longer distances, more GPU cost.
        public float LodBias;

        // Soft edge particles near surfaces. Enabled = better visuals, slight GPU cost.
        public bool SoftParticles;

        // Range 0.5-2.0. Higher = sharper image, more GPU cost. 1.0 = native resolution.
        public float RenderScale;

        // HDR rendering. Enabled = wider color/brightness range, slight GPU cost.
        public bool HDREnabled;

        // Short edge resolution in pixels (e.g., 720, 1080, 1440). Aspect ratio is auto-detected from device.
        public int ShortEdgeResolution;
    }
}